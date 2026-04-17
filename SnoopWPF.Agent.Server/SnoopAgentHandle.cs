namespace SnoopWPF.Agent.Server;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Audit;

/// <summary>
/// Represents a running SnoopWPF MCP server session. Dispose to stop the server.
/// </summary>
/// <remarks>
/// Also implements <see cref="IStartupFailureSink"/> so that the background startup
/// task can report fatal errors back to the embedding application (FX6-D1).
/// </remarks>
public sealed class SnoopAgentHandle : IDisposable, IStartupFailureSink
{
    private readonly CancellationTokenSource cts;
    private readonly SnoopWPF.Agent.Engine.SnoopInspector inspector;
    private int disposedFlag;
    private int startupFailedFlag;

    internal SnoopAgentHandle(
        CancellationTokenSource cts,
        SnoopWPF.Agent.Engine.SnoopInspector inspector,
        SessionPolicy policy)
    {
        this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Optional audit log writer. Non-null when <see cref="SnoopAgentOptions.AuditLogPath"/> is set.
    /// Disposed on handle disposal.
    /// </summary>
    internal AuditLogWriter? AuditWriter { get; set; }

    /// <summary>
    /// The immutable session policy for this session. Constructed once at session start (S1).
    /// Tool handlers read policy from this reference; nothing mutates it after construction.
    /// </summary>
    public SessionPolicy Policy { get; }

    /// <summary>
    /// When <see cref="TransportMode.Pipe"/> is used, the name of the named pipe that the server
    /// is listening on. <see langword="null"/> when <see cref="TransportMode.Stdio"/> is used.
    /// </summary>
    /// <remarks>
    /// The embedding application is responsible for securely delivering this value to its
    /// client. The pipe enforces <c>CurrentUserOnly</c> ACL; additionally the client must
    /// supply the matching <see cref="SessionToken"/> during the opening handshake.
    /// </remarks>
    public string? PipeName { get; internal set; }

    /// <summary>
    /// When <see cref="TransportMode.Pipe"/> is used, the 256-bit (64 hex-character) session
    /// token that the MCP client must echo back during the opening handshake.
    /// <see langword="null"/> when <see cref="TransportMode.Stdio"/> is used.
    /// </summary>
    /// <remarks>
    /// Treat this value like a password. Do not log it or write it to stdout.
    /// The embedding application decides how to deliver it to its client (e.g., in-process
    /// reference, secure IPC, environment variable scoped to the child process, etc.).
    /// </remarks>
    public string? SessionToken { get; internal set; }

    /// <summary>
    /// <see langword="true"/> once the background server loop has started handling MCP
    /// requests. Set immediately before <c>McpServer.RunAsync</c> is entered (FX6-D1).
    /// </summary>
    public bool IsStarted { get; internal set; }

    /// <summary>
    /// UTC timestamp recorded when <see cref="IsStarted"/> was first set to
    /// <see langword="true"/>. Used by <c>wpf_diagnostics</c> to compute uptime (FX6-D3).
    /// <see langword="null"/> until the server loop starts.
    /// </summary>
    public DateTimeOffset? StartedAt { get; internal set; }

    /// <summary>
    /// The exception that caused startup to fail, or <see langword="null"/> if startup
    /// succeeded or has not yet completed. Set by <see cref="IStartupFailureSink.OnStartupFailed"/>
    /// (FX6-D1).
    /// </summary>
    public Exception? StartupException { get; private set; }

    // -------------------------------------------------------------------------
    // IStartupFailureSink (FX6-D1)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    void IStartupFailureSink.OnStartupFailed(Exception ex)
    {
        // Idempotent: only the first caller wins.
        if (Interlocked.Exchange(ref this.startupFailedFlag, 1) != 0)
        {
            return;
        }

        this.StartupException = ex;

        // Emit a framed MCP JSON-RPC error notification to stderr so stdio MCP clients
        // can observe it. Console.Out is redirected to TextWriter.Null in CoLocated mode;
        // stderr is the only safe channel (MCP spec does not reserve stderr for protocol).
        try
        {
            var errorPayload = new StartupErrorNotification
            {
                Jsonrpc = "2.0",
                Id = null,
                Error = new StartupErrorDetail
                {
                    Code = -32603,
                    Message = $"SnoopWPF.Agent startup failed: {ex.GetType().Name}: {ex.Message}",
                },
            };
            var json = JsonSerializer.Serialize(errorPayload, StartupErrorSerializerOptions.Options);
            var body = Encoding.UTF8.GetBytes(json);
            // Use Content-Length framing so MCP stdio parsers can skip the frame cleanly.
            var header = $"Content-Length: {body.Length}\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            var stderr = Console.OpenStandardError();
            stderr.Write(headerBytes, 0, headerBytes.Length);
            stderr.Write(body, 0, body.Length);
            stderr.Flush();
        }
        catch
        {
            // stderr write failure must not crash the host.
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops the MCP server and disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposedFlag, 1) == 1)
        {
            return;
        }

        this.cts.Cancel();
        this.cts.Dispose();
        this.inspector.Dispose();

        // Stop the audit writer and wait for it to drain (fire-and-forget async dispose via sync wrapper).
        if (this.AuditWriter != null)
        {
            // DisposeAsync drains the channel before closing the file.
            // We run it synchronously here because Dispose() is synchronous.
            this.AuditWriter.DisposeAsync().AsTask().GetAwaiter().GetResult();
            this.AuditWriter = null;
        }

        // Null security-sensitive string references so the GC can collect them sooner.
        // String is immutable and Array.Clear cannot zero its backing memory, but dropping
        // the references makes them unreachable and shortens the window they remain in the heap.
        this.SessionToken = null;
        this.PipeName = null;

        SnoopAgent.ClearHandle();
    }

    // -------------------------------------------------------------------------
    // Private serialization helpers (avoid taking a reference on System.Text.Json
    // at the call-site of OnStartupFailed which must not allocate or throw).
    // -------------------------------------------------------------------------

    private sealed class StartupErrorNotification
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string Jsonrpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public object? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public StartupErrorDetail Error { get; set; } = null!;
    }

    private sealed class StartupErrorDetail
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private static class StartupErrorSerializerOptions
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // use attribute-specified names
        };
    }
}
