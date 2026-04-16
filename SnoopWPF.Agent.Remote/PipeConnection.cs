namespace SnoopWPF.Agent.Remote;

using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Wraps a <see cref="NamedPipeServerStream"/> on the host side.
/// The host creates this stream; the injected agent connects as a client.
/// Performs the initial handshake: host sends <see cref="HandshakeChallenge"/>,
/// agent replies with <see cref="HandshakeResponse"/>.
/// Validates protocol version and (on Windows) verifies the client PID.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PipeConnection : IDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly FramedJsonTransport transport;
    private readonly int expectedClientPid;
    private bool disposed;

    /// <summary>
    /// Gets the pipe name (GUID string) used for the connection.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets the capabilities reported by the connected agent (populated after <see cref="HandshakeAsync"/>).
    /// </summary>
    public HandshakeResponse? RemoteHandshake { get; private set; }

    /// <summary>
    /// Creates the named pipe server and waits for the injected agent to connect.
    /// </summary>
    /// <param name="pipeName">Pipe name (should be a random GUID).</param>
    /// <param name="expectedClientPid">
    /// The PID of the injected process. After connection,
    /// <see cref="HandshakeAsync"/> will verify that the connecting client matches this PID.
    /// Pass -1 to skip PID verification.
    /// </param>
    public PipeConnection(string pipeName, int expectedClientPid)
    {
        this.PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        this.expectedClientPid = expectedClientPid;

        this.pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        this.transport = new FramedJsonTransport(this.pipe);
    }

    /// <summary>
    /// Waits for the agent to connect (up to <paramref name="ct"/>).
    /// </summary>
    public Task WaitForConnectionAsync(CancellationToken ct)
    {
        return this.pipe.WaitForConnectionAsync(ct);
    }

    /// <summary>
    /// Performs the opening handshake.
    /// Host sends <see cref="HandshakeChallenge"/>; agent responds with <see cref="HandshakeResponse"/>.
    /// Validates protocol version and client PID.
    /// Throws <see cref="SnoopException"/> with <see cref="SnoopErrorCode.ProtocolMismatch"/> on failure.
    /// </summary>
    public async Task HandshakeAsync(string sessionToken, CancellationToken ct)
    {
        this.ThrowIfDisposed();

        // 1. Verify client PID before exchanging secrets.
        if (this.expectedClientPid >= 0)
        {
            this.VerifyClientPid();
        }

        // 2. Send challenge.
        var challenge = new HandshakeChallenge
        {
            SessionToken = sessionToken,
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
        };

        await this.transport.SendAsync(challenge, ct).ConfigureAwait(false);

        // 3. Read response.
        HandshakeResponse? response;
        try
        {
            response = await this.transport.ReceiveAsync<HandshakeResponse>(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                $"Failed to read handshake response: {ex.Message}",
                innerException: ex);
        }

        if (response is null)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                "Agent closed the connection during handshake.");
        }

        // 4. Check protocol version.
        if (response.ProtocolVersion != ProtocolConstants.ProtocolVersion)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                $"Protocol version mismatch: host={ProtocolConstants.ProtocolVersion}, " +
                $"agent={response.ProtocolVersion}. {SnoopSuggestions.ProtocolMismatch}");
        }

        // 5. Verify the agent echoed back our session token.
        if (response.SessionToken != sessionToken)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                "Agent returned an incorrect session token. Possible man-in-the-middle or wrong process.");
        }

        this.RemoteHandshake = response;
    }

    /// <summary>
    /// Sends a framed request over the pipe.
    /// </summary>
    internal Task SendRequestAsync(PipeRequest request, CancellationToken ct)
    {
        this.ThrowIfDisposed();
        return this.transport.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends a cancel frame over the pipe.
    /// </summary>
    internal Task SendCancelAsync(PipeCancelPayload cancel, CancellationToken ct)
    {
        this.ThrowIfDisposed();
        return this.transport.SendAsync(cancel, ct);
    }

    /// <summary>
    /// Reads the next response frame from the pipe.
    /// Returns <see langword="null"/> if the connection was closed cleanly.
    /// </summary>
    internal Task<PipeResponse?> ReceiveResponseAsync(CancellationToken ct)
    {
        this.ThrowIfDisposed();
        return this.transport.ReceiveAsync<PipeResponse>(ct);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.pipe.Dispose();
    }

    // -------------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(PipeConnection));
        }
    }

    [SupportedOSPlatform("windows")]
    private void VerifyClientPid()
    {
        if (!this.pipe.IsConnected)
        {
            return;
        }

        int actualPid = GetNamedPipeClientProcessId(this.pipe.SafePipeHandle.DangerousGetHandle());
        if (actualPid < 0)
        {
            // Could not read client PID — log and continue rather than blocking injection.
            return;
        }

        if (actualPid != this.expectedClientPid)
        {
            throw new SnoopException(
                SnoopErrorCode.ProtocolMismatch,
                $"Named pipe client PID mismatch: expected={this.expectedClientPid}, " +
                $"actual={actualPid}. Rejecting connection.");
        }
    }

    private static int GetNamedPipeClientProcessId(IntPtr pipeHandle)
    {
        if (!NativeMethods.GetNamedPipeClientProcessId(pipeHandle, out uint pid))
        {
            return -1;
        }

        return (int)pid;
    }

    // -------------------------------------------------------------------------

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetNamedPipeClientProcessId(IntPtr hNamedPipe, out uint clientProcessId);
    }
}
