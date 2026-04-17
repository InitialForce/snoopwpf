namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Exposes the pending audit entry count to the <c>WpfDiagnosticsTool</c>
/// without leaking the internal <c>AuditLogWriter</c> implementation (FX6-D3).
/// </summary>
public interface IAuditDepthProvider
{
    /// <summary>
    /// Number of audit entries queued but not yet flushed to disk.
    /// Returns 0 when the queue is empty.
    /// </summary>
    int PendingEntryCount { get; }
}
