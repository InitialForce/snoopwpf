namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Session policy snapshot returned as part of <see cref="AgentDiagnosticsDto"/>.
/// Contains the policy settings active for the current session.
/// </summary>
[DataContract]
public sealed class SessionPolicySnapshotDto
{
    /// <summary><see langword="true"/> when property mutation is enabled for this session.</summary>
    [DataMember(Name = "enableMutation")]
    public bool EnableMutation { get; init; }

    /// <summary><see langword="true"/> when UI Automation input is enabled for this session.</summary>
    [DataMember(Name = "enableAutomation")]
    public bool EnableAutomation { get; init; }

    /// <summary><see langword="true"/> when sensitive property retention is allowed for this session.</summary>
    [DataMember(Name = "allowSensitiveRetention")]
    public bool AllowSensitiveRetention { get; init; }
}

/// <summary>
/// Self-health diagnostic payload returned by the <c>wpf_diagnostics</c> MCP tool (FX6-D3).
/// Provides a concise snapshot of agent health, resource utilisation, and session configuration.
/// Use this tool as a first step before any inspection session to verify the agent is functional.
/// </summary>
[DataContract]
public sealed class AgentDiagnosticsDto
{
    /// <summary>Semantic version of the running agent assembly (e.g. <c>6.2.0</c>).</summary>
    [DataMember(Name = "agentVersion")]
    public string AgentVersion { get; init; } = string.Empty;

    /// <summary>
    /// Session mode: <c>"CoLocated"</c>, <c>"Brokered"</c>, or <c>"Injection"</c>.
    /// </summary>
    [DataMember(Name = "mode")]
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// <see langword="true"/> when the WPF Dispatcher is running and accepts work.
    /// <see langword="false"/> when <c>HasShutdownStarted</c> or <c>HasShutdownFinished</c>
    /// is detected, or when a Dispatcher probe times out.
    /// </summary>
    [DataMember(Name = "dispatcherHealthy")]
    public bool DispatcherHealthy { get; init; }

    /// <summary>
    /// Estimated number of pending items in the Dispatcher queue at the time of the call.
    /// Returns 0 when the queue is empty or when the count cannot be determined.
    /// </summary>
    [DataMember(Name = "dispatcherQueueLength")]
    public int DispatcherQueueLength { get; init; }

    /// <summary>Number of blobs currently stored in the in-process BlobStore.</summary>
    [DataMember(Name = "blobStoreCount")]
    public int BlobStoreCount { get; init; }

    /// <summary>Total byte footprint of all blobs currently in the in-process BlobStore.</summary>
    [DataMember(Name = "blobStoreBytes")]
    public long BlobStoreBytes { get; init; }

    /// <summary>
    /// Number of audit entries queued but not yet flushed to disk. 0 when audit logging
    /// is disabled or when the writer has fully drained.
    /// </summary>
    [DataMember(Name = "auditLogDepth")]
    public int AuditLogDepth { get; init; }

    /// <summary>Active session policy settings for this session.</summary>
    [DataMember(Name = "sessionPolicy")]
    public SessionPolicySnapshotDto SessionPolicy { get; init; } = new SessionPolicySnapshotDto();

    /// <summary>
    /// Number of seconds the agent has been running since <c>StartAsync</c> returned
    /// a handle with <c>IsStarted = true</c>.
    /// </summary>
    [DataMember(Name = "uptimeSeconds")]
    public double UptimeSeconds { get; init; }
}
