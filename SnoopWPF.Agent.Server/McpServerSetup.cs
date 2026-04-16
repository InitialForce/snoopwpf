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
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Engine;
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
                return RunWithStdioAsync(inspector, policy, serverOptions, ct);

            case TransportMode.Pipe:
                // PipeName and SessionToken were resolved in SnoopAgent.StartCoLocated() before the
                // background task was launched, so handle properties are guaranteed non-null here.
                var pipeName = handle.PipeName!;
                var sessionToken = handle.SessionToken!;
                return RunWithPipeAsync(inspector, policy, serverOptions, pipeName, sessionToken, ct);

            default:
                throw new ArgumentOutOfRangeException(nameof(options), $"Unknown transport: {options.Transport}");
        }
    }

    private static async Task RunWithStdioAsync(
        ISnoopInspector inspector,
        SessionPolicy policy,
        McpServerOptions serverOptions,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector, policy);
        var sp = services.BuildServiceProvider();

        // StdioServerTransport reads from Console.In / writes to Console.Out.
        var transport = new StdioServerTransport(serverOptions);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    private static async Task RunWithPipeAsync(
        ISnoopInspector inspector,
        SessionPolicy policy,
        McpServerOptions serverOptions,
        string pipeName,
        string sessionToken,
        CancellationToken ct)
    {
        var services = BuildServiceCollection(inspector, policy);
        var sp = services.BuildServiceProvider();

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
        // Server-speaks-first: send HandshakeChallenge, then verify the echoed response.
        bool handshakeOk = await PerformPipeHandshakeAsync(pipeServer, sessionToken, ct)
            .ConfigureAwait(false);

        if (!handshakeOk)
        {
            // Handshake failed (bad token or timeout). Close this connection and stop.
            // The warning is logged without any secret material.
            Trace.TraceWarning("SnoopWPF.Agent pipe handshake failed. Connection rejected.");
            return;
        }

        var transport = new StreamServerTransport(pipeServer, pipeServer);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the server-side named-pipe handshake.
    /// <list type="number">
    ///   <item>Sends a <see cref="HandshakeChallenge"/> containing the session token and protocol version.</item>
    ///   <item>Reads a <see cref="HandshakeResponse"/> with a 5-second timeout.</item>
    ///   <item>Verifies the echoed session token via constant-time comparison and checks protocol version.</item>
    /// </list>
    /// Returns <see langword="true"/> on success, <see langword="false"/> on any mismatch or timeout.
    /// </summary>
    private static async Task<bool> PerformPipeHandshakeAsync(
        Stream pipeStream,
        string sessionToken,
        CancellationToken ct)
    {
        try
        {
            // Send challenge.
            var challenge = new HandshakeChallenge
            {
                SessionToken = sessionToken,
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

            // Verify the echoed session token using constant-time comparison.
            if (!ConstantTimeTokenEquals(response.SessionToken, sessionToken))
            {
                Trace.TraceWarning("SnoopWPF.Agent pipe handshake: session token mismatch.");
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

    /// <summary>
    /// Constant-time string comparison using <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// to prevent timing-oracle attacks on the session token.
    /// Returns false if either argument is null or lengths differ.
    /// </summary>
    private static bool ConstantTimeTokenEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);

        // Length is not secret — differing lengths are an immediate mismatch.
        if (aBytes.Length != bBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="IServiceCollection"/> with <see cref="ISnoopInspector"/> and all tool types
    /// from <c>SnoopWPF.Agent.Tools</c> registered.
    /// </summary>
    private static IServiceCollection BuildServiceCollection(ISnoopInspector inspector, SessionPolicy policy)
    {
        var services = new ServiceCollection();

        // Register ISnoopInspector so tool constructors can receive it via DI.
        services.AddSingleton<ISnoopInspector>(inspector);

        // Register SessionPolicy so future tool handlers can receive it via DI.
        services.AddSingleton<SessionPolicy>(policy);

        // Register BlobStore so FetchBlobTool (and future blob-producing tools) share one store.
        services.AddSingleton<BlobStore>();

        // Register every tool class from the Tools assembly via the MCP builder.
        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        return services;
    }
}
