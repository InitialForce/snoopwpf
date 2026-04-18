namespace SnoopWPF.Agent.Server;

// BrokerPipeServer — pipe SERVER for Mode 2 (warm-attach) connections.
//
// Responsibilities:
//   1. Create the named pipe with a hardened DACL:
//      - Owner = current user SID
//      - Single ALLOW ACE (FullControl) for current user
//      - SetAccessRuleProtection(isProtected: true, preserveInheritance: false)
//        → blocks any inherited ACEs
//   2. Wait for exactly one broker to connect and complete the HMAC-SHA256 handshake.
//   3. After the first authenticated client, enforce ALREADY_ATTACHED (R7):
//      any subsequent connection attempt receives a structured ALREADY_ATTACHED frame
//      then the connection is closed.
//   4. Return the authenticated pipe stream to the caller (SnoopAgent) for MCP dispatch.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Owns the named-pipe SERVER lifecycle for Mode 2 (warm-attach) brokered connections.
/// Applies a hardened DACL, performs the HMAC-SHA256 handshake, and enforces the
/// ALREADY_ATTACHED constraint (R7): at most one authenticated client per session.
/// </summary>
internal sealed class BrokerPipeServer : IDisposable
{
    // Frame type constant sent to rejected second-connection clients.
    private const string AlreadyAttachedType = "ALREADY_ATTACHED";

    // maxInstances=2: allows one primary session + one transient rejection listener.
    // The OS enforces the pipe instance count; the second slot is consumed only briefly
    // (for the duration of sending the rejection frame), then released.
    private const int MaxPipeInstances = 2;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    private readonly string pipeName;
    private readonly string sessionToken;
    private int disposedFlag;

    internal BrokerPipeServer(string pipeName, string sessionToken)
    {
        this.pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        this.sessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
    }

    /// <summary>
    /// Creates the hardened pipe DACL: protected (no inheritance), single ALLOW ACE for
    /// the current user SID (FullControl).
    /// </summary>
    /// <remarks>
    /// <c>SetAccessRuleProtection(isProtected: true, preserveInheritance: false)</c>
    /// removes all inherited ACEs so no parent-container permissions can bleed through.
    /// Only the explicit ALLOW rule for the current user SID remains.
    /// </remarks>
    internal static PipeSecurity CreateHardenedPipeSecurity()
    {
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine current user SID.");

        var ps = new PipeSecurity();

        // Block all inherited ACEs; start with a clean DACL.
        ps.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Single ALLOW rule: current user SID gets full pipe control.
        ps.AddAccessRule(new PipeAccessRule(
            currentUserSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return ps;
    }

    /// <summary>
    /// Creates a <see cref="NamedPipeServerStream"/> with the hardened DACL applied.
    /// Uses <see cref="NamedPipeServerStreamAcl.Create"/> (available in .NET 5+)
    /// so the security descriptor is set atomically at creation time — not after the
    /// pipe handle is already open (which would be a TOCTOU window).
    /// </summary>
    internal static NamedPipeServerStream CreateHardenedPipe(
        string pipeName,
        int maxInstances = MaxPipeInstances)
    {
        var ps = CreateHardenedPipeSecurity();

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: ps);
    }

    /// <summary>
    /// Waits for the first broker to connect, performs the HMAC handshake, and then starts
    /// the ALREADY_ATTACHED guard loop (which rejects subsequent connections).
    /// </summary>
    /// <returns>
    /// The authenticated <see cref="NamedPipeServerStream"/> on success, or
    /// <see langword="null"/> if the handshake failed or the operation was cancelled.
    /// The caller owns the returned stream and must dispose it.
    /// </returns>
    internal async Task<NamedPipeServerStream?> AcceptAuthenticatedClientAsync(CancellationToken ct)
    {
        var pipeServer = CreateHardenedPipe(this.pipeName, maxInstances: MaxPipeInstances);

        Trace.TraceInformation(
            "BrokerPipeServer: waiting for broker connection on '{0}'.", this.pipeName);

        try
        {
            await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Trace.TraceInformation("BrokerPipeServer: listener cancelled before broker connected.");
            pipeServer.Dispose();
            return null;
        }

        // HMAC handshake — server speaks first: sends nonce, reads HMAC proof.
        bool handshakeOk = await McpServerSetup.PerformPipeHandshakeAsync(pipeServer, this.sessionToken, ct)
            .ConfigureAwait(false);

        if (!handshakeOk)
        {
            Trace.TraceWarning("BrokerPipeServer: handshake failed — connection rejected.");
            pipeServer.Dispose();
            return null;
        }

        Trace.TraceInformation("BrokerPipeServer: broker authenticated. Starting ALREADY_ATTACHED guard.");

        // Start the ALREADY_ATTACHED guard: any subsequent connection attempt will receive
        // a structured rejection frame. This runs as a fire-and-forget background task tied
        // to the same CancellationToken — it exits when ct is cancelled or the pipe OS handle
        // is released (which happens when the primary pipeServer is disposed on session end).
        _ = Task.Run(() => this.RunAlreadyAttachedGuardAsync(ct), ct);

        return pipeServer;
    }

    /// <summary>
    /// Runs the ALREADY_ATTACHED rejection guard.  Opens a second pipe server instance
    /// on the same pipe name (using the second slot in MaxPipeInstances=2), waits for
    /// a connection, sends the structured rejection frame, then closes the connection.
    /// Repeats until the CancellationToken fires (primary session ended / handle disposed).
    /// </summary>
    private async Task RunAlreadyAttachedGuardAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? guard = null;
            try
            {
                // Create a second server instance to capture subsequent connection attempts.
                guard = CreateHardenedPipe(this.pipeName, maxInstances: MaxPipeInstances);

                await guard.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // A second client connected — send ALREADY_ATTACHED frame then close.
                Trace.TraceWarning(
                    "BrokerPipeServer: second connection attempt rejected with ALREADY_ATTACHED.");

                await SendAlreadyAttachedFrameAsync(guard, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Primary session ended — exit guard loop.
                break;
            }
            catch (Exception ex)
            {
                // Guard pipe creation/write error — log and loop.
                Trace.TraceWarning("BrokerPipeServer guard error: {0}", ex.Message);
            }
            finally
            {
                guard?.Dispose();
            }
        }

        Trace.TraceInformation("BrokerPipeServer: ALREADY_ATTACHED guard exited.");
    }

    /// <summary>
    /// Sends a structured <c>ALREADY_ATTACHED</c> error frame to a newly-connected client,
    /// then flushes and returns.  The caller disposes the stream.
    /// </summary>
    private static async Task SendAlreadyAttachedFrameAsync(Stream stream, CancellationToken ct)
    {
        var frame = new BrokerErrorFrame
        {
            Type = AlreadyAttachedType,
            Message = "A broker session is already active on this pipe. Only one concurrent session is allowed.",
        };

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        byte[] lengthBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuf, body.Length);

        try
        {
            await stream.WriteAsync(lengthBuf, 0, 4, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Client may have disconnected before we could write — non-fatal.
            Trace.TraceWarning("BrokerPipeServer: failed to write ALREADY_ATTACHED frame: {0}", ex.Message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Interlocked.Exchange(ref this.disposedFlag, 1);
        // No unmanaged resources held directly here — pipe streams created via
        // AcceptAuthenticatedClientAsync are owned and disposed by the caller.
    }
}

/// <summary>
/// JSON frame sent to a second (rejected) broker connection attempt.
/// </summary>
internal sealed class BrokerErrorFrame
{
    /// <summary>Error type discriminator, e.g. "ALREADY_ATTACHED".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Human-readable message.</summary>
    public string Message { get; set; } = string.Empty;
}
