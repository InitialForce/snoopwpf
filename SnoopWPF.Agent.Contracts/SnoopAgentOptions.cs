namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Transport mode for the MCP server.
/// </summary>
public enum TransportMode
{
    /// <summary>Standard input/output (stdio) transport.</summary>
    Stdio,

    /// <summary>Named pipe transport.</summary>
    Pipe,
}

/// <summary>
/// Options controlling how <see cref="SnoopWPF.Agent.Server.SnoopAgent"/> creates and runs the MCP server.
/// </summary>
public sealed class SnoopAgentOptions
{
    /// <summary>
    /// Transport mode. Default is <see cref="TransportMode.Stdio"/>.
    /// </summary>
    public TransportMode Transport { get; init; } = TransportMode.Stdio;

    /// <summary>
    /// Named-pipe name used when <see cref="Transport"/> is <see cref="TransportMode.Pipe"/>.
    /// When null or empty a random name is auto-generated as <c>snoop-agent-{guid}</c>.
    /// </summary>
    public string? PipeName { get; init; }

    /// <summary>
    /// Session token used for the named-pipe handshake when <see cref="Transport"/> is
    /// <see cref="TransportMode.Pipe"/>. When null or empty a cryptographically random
    /// 256-bit token is generated automatically.
    /// </summary>
    /// <remarks>
    /// The generated (or supplied) token is exposed via <see cref="SnoopWPF.Agent.Server.SnoopAgentHandle.SessionToken"/>
    /// so that the embedding application can deliver it to its client.
    /// Treat the token like a password — do not log it or write it to stdout.
    /// </remarks>
    public string? SessionToken { get; init; }

    /// <summary>
    /// Per-operation Dispatcher timeout in milliseconds. Default 5000 ms.
    /// </summary>
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Whether property mutation (SetProperty) is enabled. Default false.
    /// </summary>
    public bool EnableMutation { get; init; } = false;

    /// <summary>
    /// Whether sensitive property values are redacted. Default true (safe by default).
    /// </summary>
    /// <remarks>
    /// In injection mode this is always forced to <see langword="true"/> by
    /// <see cref="SessionPolicy.Create"/> regardless of the value set here (MF-11).
    /// </remarks>
    public bool EnableRedaction { get; init; } = true;

    /// <summary>
    /// Maximum input tier allowed for this session. Default <see cref="InputTier.L0"/>.
    /// </summary>
    /// <remarks>
    /// In injection mode this is always capped to <see cref="InputTier.L0ReadOnly"/> by
    /// <see cref="SessionPolicy.Create"/> regardless of the value set here (S7).
    /// </remarks>
    public InputTier MaxTier { get; init; } = InputTier.L0;

    /// <summary>
    /// Whether UI Automation-based input is enabled. Default false (safe by default in MVP).
    /// </summary>
    public bool EnableAutomation { get; init; } = false;

    /// <summary>
    /// Whether sensitive values may be retained in responses (e.g., clipboard, secure fields).
    /// Default false (safe by default).
    /// </summary>
    public bool AllowSensitiveRetention { get; init; } = false;

    /// <summary>
    /// Maximum allowed <c>timeoutMs</c> for <c>wpf_wait_for_property</c> calls.
    /// Requests that exceed this ceiling are rejected with <c>InvalidArgument</c> rather than
    /// being allowed to hold the concurrency semaphore for an unbounded duration (FX6-A1).
    /// Default: 30 000 ms (30 seconds).
    /// </summary>
    public int MaxWaitForPropertyMs { get; init; } = 30_000;

    /// <summary>
    /// TTL applied to blobs stored in the in-process <c>BlobStore</c>
    /// (e.g. screenshot PNG data). Default is 60 seconds.
    /// </summary>
    /// <remarks>
    /// Agents should call <c>wpf_fetch_blob</c> within this window after receiving a
    /// <c>blobRef</c> from <c>wpf_capture_screenshot</c>. Re-run the originating tool
    /// to obtain a fresh reference after expiry.
    /// </remarks>
    public TimeSpan BlobTtl { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum number of blobs retained in the in-process BlobStore at any one time.
    /// When the limit is exceeded, the oldest entry (smallest <c>expiresAt</c>) is evicted
    /// before the new entry is inserted. Default is 64.
    /// </summary>
    public int BlobStoreMaxCount { get; init; } = 64;

    /// <summary>
    /// Maximum total byte footprint of blobs retained in the in-process BlobStore.
    /// When the limit is exceeded, the oldest entry (smallest <c>expiresAt</c>) is evicted
    /// before the new entry is inserted. Default is 128 MB.
    /// </summary>
    public long BlobStoreMaxBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>
    /// Opt-in session identifier that enables the per-session HMAC-chained audit log.
    /// When non-null, every tool call produces an <see cref="SnoopWPF.Agent.Contracts.Audit.AuditEntry"/>
    /// appended as JSONL to <c>%LOCALAPPDATA%\SnoopWPF\audit\{AuditLogPath}.jsonl</c>.
    /// When <see langword="null"/> (the default) audit logging is fully disabled.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> provides a tamper-evident record of every MCP tool invocation
    /// for compliance, debugging, and security review. Each entry carries an HMAC-SHA256
    /// chain tag so that gaps or tampering can be detected offline. See PRD §9.4 for the
    /// chain format specification.</para>
    ///
    /// <para><b>Value semantics:</b> the string is used verbatim as the log file stem — it is
    /// NOT interpreted as a file-system path. Only alphanumeric characters, hyphens (<c>-</c>),
    /// and underscores (<c>_</c>) are preserved; any other character is replaced with an
    /// underscore by <c>AuditLogWriter</c>. Use a stable, unique per-session value such as a
    /// GUID string (e.g. <c>Guid.NewGuid().ToString()</c>).</para>
    ///
    /// <para><b>File location:</b> always written to
    /// <c>%LOCALAPPDATA%\SnoopWPF\audit\{sanitised-value}.jsonl</c> on the target machine.
    /// The directory is created automatically. Do not pass a rooted path or directory
    /// separator characters — the writer will sanitise them but the result will be a flat
    /// file name, not a nested directory.</para>
    ///
    /// <para><b>Security:</b> the log file is created with an OWNER_ONLY ACL (Windows,
    /// net8+) so that other local user accounts cannot read or tamper with the log.
    /// The per-session HMAC key is a cryptographically random 32-byte value held only
    /// in memory; it is not persisted anywhere, so the chain cannot be forged after the
    /// session ends.</para>
    ///
    /// <para><b>Brokered mode:</b> in brokered mode the audit log runs TARGET-SIDE only.
    /// The broker process must not construct its own <c>AuditLogWriter</c>; doing so would
    /// cause two writers to race on the same <c>.jsonl</c> file and corrupt the HMAC chain.</para>
    /// </remarks>
    public string? AuditLogPath { get; init; }

    /// <summary>
    /// When <see langword="true"/> and the primary <see cref="AuditLogPath"/> directory is
    /// not writable, the agent falls back to
    /// <c>%LOCALAPPDATA%\SnoopWPF.Agent\audit\{sessionId}.jsonl</c> instead of aborting
    /// start-up with <c>SnoopException(AuditUnwritable)</c>.
    ///
    /// Default is <see langword="false"/> (fail-fast) so that audit silencing is opt-in.
    /// Only effective when <see cref="AuditLogPath"/> is non-null. (FX6-D2)
    /// </summary>
    public bool AllowAuditFallback { get; init; } = false;
}
