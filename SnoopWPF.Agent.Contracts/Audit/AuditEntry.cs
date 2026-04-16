// SnoopWPF.Agent.Contracts/Audit/AuditEntry.cs
// FD-7: frozen AuditEntry signature — do NOT change field names or types without updating
// the HMAC chain computation in AuditLogWriter and migrating existing log files.

namespace SnoopWPF.Agent.Contracts.Audit;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

/// <summary>
/// Immutable record representing one entry in the HMAC-chained audit log.
/// Field names are part of the on-disk format (FD-7) — treat as frozen API.
/// </summary>
[DataContract]
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated via DataContractJsonSerializer and in tests (InternalsVisibleTo).")]
internal sealed record AuditEntry
{
    /// <summary>Monotonically-increasing sequence number within the session.</summary>
    [DataMember(Name = "seq")]
    public long Seq { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    [DataMember(Name = "at")]
    public DateTimeOffset At { get; init; }

    /// <summary>Name of the MCP tool that produced this entry.</summary>
    [DataMember(Name = "toolName")]
    public string ToolName { get; init; } = string.Empty;

    /// <summary>Session identifier, matches the log file name.</summary>
    [DataMember(Name = "sessionId")]
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Outcome of the tool invocation: "ok" or "fail".</summary>
    [DataMember(Name = "outcome")]
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Optional human-readable reason; sanitized (newlines replaced, NUL dropped, 256-char cap).</summary>
    [DataMember(Name = "reason")]
    public string? Reason { get; init; }

    /// <summary>Monotonically-increasing counter used as nonce in the HMAC computation.</summary>
    [DataMember(Name = "counterNonce")]
    public long CounterNonce { get; init; }

    /// <summary>Hex-encoded HMAC-SHA256 over entryJson || prevHmac || sessionKey || counterNonce.</summary>
    [DataMember(Name = "hmac")]
    public string Hmac { get; init; } = string.Empty;
}
