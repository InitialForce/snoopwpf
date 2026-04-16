namespace SnoopWPF.Agent.Server;

using System;
using System.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Represents a running SnoopWPF MCP server session. Dispose to stop the server.
/// </summary>
public sealed class SnoopAgentHandle : IDisposable
{
    private readonly CancellationTokenSource cts;
    private readonly SnoopWPF.Agent.Engine.SnoopInspector inspector;
    private bool disposed;

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
    /// Stops the MCP server and disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cts.Cancel();
        this.cts.Dispose();
        this.inspector.Dispose();

        SnoopAgent.ClearHandle();
    }
}
