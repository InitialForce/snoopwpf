namespace SnoopWPF.Agent.Server;

using System;
using System.Threading;

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
        SnoopWPF.Agent.Engine.SnoopInspector inspector)
    {
        this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

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
