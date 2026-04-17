namespace SnoopWPF.Agent.Server;

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Audit;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Engine.Audit;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// Internal wiring: builds and runs the MCP server.
/// </summary>
internal static class McpServerSetup
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Builds the MCP server's service collection with all tools registered,
    /// then starts the server loop on a background thread.
    /// </summary>
    internal static Task RunServerAsync(
        ISnoopInspector inspector,
        SnoopAgentOptions options,
        SessionPolicy policy,
        SnoopAgentHandle handle,
        CancellationToken ct)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "snoop-wpf",
                Version = version,
            },
        };

        switch (options.Transport)
        {
            case TransportMode.Stdio:
                return RunWithStdioAsync(inspector, options, policy, handle.AuditWriter, serverOptions, ct);

            case TransportMode.Pipe:
                // PipeName and SessionToken were resolved in SnoopAgent.StartCoLocated() before the
                // background task was launched, so handle properties are guaranteed non-null here.
                var pipeName = handle.PipeName!;
                var sessionToken = handle.SessionToken!;
                return RunWithPipeAsync(inspector, options, policy, handle.AuditWriter, serverOptions, pipeName, sessionToken, ct);

            default:
                throw new ArgumentOutOfRangeException(nameof(options), $"Unknown transport: {options.Transport}");
        }
    }

    private static async Task RunWithStdioAsync(
        ISnoopInspector inspector,
        SnoopAgentOptions agentOptions,
        SessionPolicy policy,
        AuditLogWriter? auditWriter,
        McpServerOptions serverOptions,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector, agentOptions, policy, auditWriter);
        await using var sp = services.BuildServiceProvider();

        await EmitSessionStartEntryAsync(auditWriter, ct).ConfigureAwait(false);

        // StdioServerTransport reads from Console.In / writes to Console.Out.
        var transport = new StdioServerTransport(serverOptions);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    private static async Task RunWithPipeAsync(
        ISnoopInspector inspector,
        SnoopAgentOptions agentOptions,
        SessionPolicy policy,
        AuditLogWriter? auditWriter,
        McpServerOptions serverOptions,
        string pipeName,
        string sessionToken,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector, agentOptions, policy, auditWriter);
        await using var sp = services.BuildServiceProvider();

        // PipeOptions.CurrentUserOnly restricts the pipe ACL to the current Windows user,
        // preventing other local accounts from connecting.
        await using var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        // NOTE: NuGet-mode agent accepts a single client per lifetime.
        // When the client disconnects, the pipe closes and the agent terminates.
        // To support multiple sessions in the same host process, wrap this in a loop
        // that re-creates the NamedPipeServerStream on each iteration.
        Trace.TraceInformation("SnoopWPF.Agent pipe server waiting for connection.");
        await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

        // Perform session-token handshake before handing the stream to the MCP layer.
        // Server-speaks-first: send HandshakeChallenge (nonce), then verify HMAC proof.
        bool handshakeOk = await PerformPipeHandshakeAsync(pipeServer, sessionToken, ct)
            .ConfigureAwait(false);

        if (!handshakeOk)
        {
            // Handshake failed (bad proof or timeout). Close this connection and stop.
            // The warning is logged without any secret material.
            Trace.TraceWarning("SnoopWPF.Agent pipe handshake failed. Connection rejected.");
            return;
        }

        await EmitSessionStartEntryAsync(auditWriter, ct).ConfigureAwait(false);

        var transport = new StreamServerTransport(pipeServer, pipeServer);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the server-side named-pipe handshake using a nonce+HMAC challenge/response.
    /// <list type="number">
    ///   <item>Generates a fresh 16-byte random nonce.</item>
    ///   <item>Sends a <see cref="HandshakeChallenge"/> containing the nonce and protocol version (no token).</item>
    ///   <item>Reads a <see cref="HandshakeResponse"/> with a 5-second timeout.</item>
    ///   <item>Verifies the HMAC proof via <see cref="CryptographicOperations.FixedTimeEquals"/> and checks protocol version.</item>
    /// </list>
    /// Returns <see langword="true"/> on success, <see langword="false"/> on any mismatch or timeout.
    /// The session token is never transmitted over the pipe in either direction.
    /// </summary>
    internal static async Task<bool> PerformPipeHandshakeAsync(
        Stream pipeStream,
        string sessionToken,
        CancellationToken ct)
    {
        try
        {
            // Generate a fresh nonce for this connection; never reuse.
            byte[] nonce = RandomNumberGenerator.GetBytes(16);

            // Send challenge — nonce only, token stays server-side.
            var challenge = new HandshakeChallenge
            {
                Nonce = nonce,
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
            };
            await SendFramedJsonAsync(pipeStream, challenge, ct).ConfigureAwait(false);

            // Read response with a 5-second timeout.
            HandshakeResponse? response;
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromMilliseconds(ProtocolConstants.HandshakeTimeoutMs));
            try
            {
                response = await ReceiveFramedJsonAsync<HandshakeResponse>(pipeStream, handshakeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Trace.TraceWarning("SnoopWPF.Agent pipe handshake timed out.");
                return false;
            }

            if (response is null)
            {
                Trace.TraceWarning("SnoopWPF.Agent pipe handshake: client closed connection before responding.");
                return false;
            }

            // Verify protocol version.
            if (response.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                Trace.TraceWarning(
                    "SnoopWPF.Agent pipe handshake: protocol version mismatch (expected {0}, got {1}).",
                    ProtocolConstants.ProtocolVersion,
                    response.ProtocolVersion);
                return false;
            }

            // Compute expected HMAC: HMACSHA256(key=sessionTokenBytes, data=nonce).
            byte[] sessionTokenBytes = Encoding.UTF8.GetBytes(sessionToken);
            byte[] expectedHmac = HMACSHA256.HashData(sessionTokenBytes, nonce);

            // Constant-time comparison to prevent timing oracle attacks.
            if (response.ProofHmac is null ||
                response.ProofHmac.Length != expectedHmac.Length ||
                !CryptographicOperations.FixedTimeEquals(response.ProofHmac, expectedHmac))
            {
                Trace.TraceWarning("SnoopWPF.Agent pipe handshake: HMAC proof mismatch.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceWarning("SnoopWPF.Agent pipe handshake error: {0}", ex.GetType().Name);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Minimal framing helpers (4-byte LE length prefix + UTF-8 JSON body).
    // These are used ONLY for the handshake phase (SendFramedJsonAsync /
    // ReceiveFramedJsonAsync). After the handshake succeeds, StreamServerTransport
    // takes ownership of the stream and uses the standard MCP JSON-RPC
    // line-delimited protocol — these helpers are not involved in that phase.
    // The duplication from FramedJsonTransport (SnoopWPF.Agent.Remote) is intentional
    // to avoid a cross-project reference from Server to Remote.
    // -------------------------------------------------------------------------

    private static async Task SendFramedJsonAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

        byte[] lengthBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuf, body.Length);

        await stream.WriteAsync(lengthBuf, 0, 4, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<T?> ReceiveFramedJsonAsync<T>(Stream stream, CancellationToken ct)
    {
        // Read 4-byte length prefix.
        byte[] lengthBuf = new byte[4];
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await stream.ReadAsync(lengthBuf, bytesRead, 4 - bytesRead, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return default; // Clean EOF.
            }

            bytesRead += n;
        }

        int frameLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
        if (frameLength < 0 || frameLength > ProtocolConstants.MaxFrameSize)
        {
            throw new InvalidOperationException(
                $"Handshake frame length {frameLength} is invalid.");
        }

        byte[] body = new byte[frameLength];
        int offset = 0;
        while (offset < frameLength)
        {
            int n = await stream.ReadAsync(body, offset, frameLength - offset, ct).ConfigureAwait(false);
            if (n == 0)
            {
                throw new EndOfStreamException("Connection closed while reading handshake frame body.");
            }

            offset += n;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Brokered-mode entry point (reconnect loop)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Brokered-mode named-pipe server with a reconnect loop.
    /// Opens a <see cref="NamedPipeServerStream"/> (CurrentUserOnly, maxInstances=1),
    /// performs the <see cref="PerformPipeHandshakeAsync"/> handshake, runs the MCP server
    /// until the client disconnects, then disposes the stream and recreates it for the next
    /// connection. Loops until <paramref name="ct"/> is cancelled.
    /// After a failed handshake a 250 ms backoff delay is applied before accepting a new
    /// connection, mitigating kernel pipe-handle exhaustion from tight-loop bad-token attacks.
    /// </summary>
    internal static async Task RunBrokeredPipeAsync(
        ISnoopInspector inspector,
        SessionPolicy policy,
        string pipeName,
        string sessionTokenHex,
        CancellationToken ct,
        AuditLogWriter? auditWriter = null,
        SnoopAgentOptions? agentOptions = null)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "snoop-wpf",
                Version = version,
            },
        };

        var services = BuildServiceCollection(inspector, agentOptions ?? new SnoopAgentOptions(), policy, auditWriter);
        await using var sp = services.BuildServiceProvider();

        // Reconnect loop: re-create the pipe after each client disconnect.
        // This supports broker crash-and-restart without requiring a target restart.
        while (!ct.IsCancellationRequested)
        {
            var pipeServer = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                Trace.TraceInformation("SnoopWPF.Agent (Brokered) waiting for connection.");

                try
                {
                    await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation while waiting — exit the loop cleanly.
                    return;
                }

                bool handshakeOk = await PerformPipeHandshakeAsync(pipeServer, sessionTokenHex, ct)
                    .ConfigureAwait(false);

                if (!handshakeOk)
                {
                    Trace.TraceWarning(
                        "SnoopWPF.Agent (Brokered) handshake failed. Connection rejected; waiting for next client.");
                    // Backoff before re-accepting to prevent kernel pipe-handle exhaustion
                    // from hostile clients hammering the pipe with bad tokens.
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    // Dispose and loop to accept a new connection.
                    continue;
                }

                await EmitSessionStartEntryAsync(auditWriter, ct).ConfigureAwait(false);

                Trace.TraceInformation("SnoopWPF.Agent (Brokered) client connected and authenticated.");
                var transport = new StreamServerTransport(pipeServer, pipeServer);
                await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);

                try
                {
                    await server.RunAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled — exit loop.
                    return;
                }
                catch (IOException)
                {
                    // Client disconnected mid-session — loop and wait for next connection.
                    Trace.TraceInformation(
                        "SnoopWPF.Agent (Brokered) client disconnected. Waiting for next connection.");
                }

                Trace.TraceInformation(
                    "SnoopWPF.Agent (Brokered) session ended. Re-creating pipe for next connection.");
            }
            finally
            {
                pipeServer.Dispose();
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a "session_start" audit entry to prove the writer is wired. No-op when
    /// <paramref name="auditWriter"/> is <see langword="null"/>.
    /// </summary>
    private static async Task EmitSessionStartEntryAsync(AuditLogWriter? auditWriter, CancellationToken ct)
    {
        if (auditWriter is null)
        {
            return;
        }

        var entry = new AuditEntry
        {
            Seq = 1,
            At = DateTimeOffset.UtcNow,
            ToolName = "session_start",
            SessionId = "server",
            Outcome = "ok",
            Reason = null,
            CounterNonce = 1,
            Hmac = string.Empty,
        };

        await auditWriter.Writer.WriteAsync(entry, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a <see cref="IServiceCollection"/> with <see cref="ISnoopInspector"/> and all tool types
    /// from <c>SnoopWPF.Agent.Tools</c> registered.
    /// </summary>
    private static IServiceCollection BuildServiceCollection(
        ISnoopInspector inspector,
        SnoopAgentOptions agentOptions,
        SessionPolicy policy,
        AuditLogWriter? auditWriter = null)
    {
        var services = new ServiceCollection();

        // Register ISnoopInspector so tool constructors can receive it via DI.
        services.AddSingleton<ISnoopInspector>(inspector);

        // Register SessionPolicy so future tool handlers can receive it via DI.
        services.AddSingleton<SessionPolicy>(policy);

        // Register SnoopAgentOptions so tools (e.g. CaptureScreenshotTool) can read BlobTtl.
        services.AddSingleton<SnoopAgentOptions>(agentOptions);

        // Register BlobStore using the configured BlobTtl as the sweep interval.
        // The sweep interval controls how often expired entries are purged; using BlobTtl
        // is a reasonable default so that stale blobs are cleaned up within one TTL window.
        services.AddSingleton<BlobStore>(_ => new BlobStore(agentOptions.BlobTtl));

        // Register AuditLogWriter if audit logging is enabled (N1).
        // Tools that want to emit audit entries can inject AuditLogWriter? from DI.
        if (auditWriter is not null)
        {
            services.AddSingleton<AuditLogWriter>(auditWriter);
        }

        // Register every tool class from the Tools assembly via the MCP builder.
        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        return services;
    }
}
