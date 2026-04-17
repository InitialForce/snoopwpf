namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Immutable start-time metadata injected into the DI container at agent startup.
/// Consumed by <c>WpfDiagnosticsTool</c> to compute uptime without referencing
/// the server-layer <c>SnoopAgentHandle</c>. (FX6-D3)
/// </summary>
public sealed class AgentStartInfo
{
    /// <summary>Initializes a new <see cref="AgentStartInfo"/> with the given start time.</summary>
    public AgentStartInfo(DateTimeOffset startedAt)
    {
        this.StartedAt = startedAt;
    }

    /// <summary>UTC timestamp when the agent server loop started (<c>McpServer.RunAsync</c> was entered).</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Number of seconds since the agent started.</summary>
    public double UptimeSeconds => (DateTimeOffset.UtcNow - this.StartedAt).TotalSeconds;
}
