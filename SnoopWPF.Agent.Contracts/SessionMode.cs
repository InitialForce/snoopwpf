namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Describes how the SnoopWPF MCP agent is integrated with the target WPF process.
/// </summary>
public enum SessionMode
{
    /// <summary>
    /// The agent NuGet package is embedded directly in the target WPF application.
    /// Caller controls all policy choices (redaction, tier, automation).
    /// </summary>
    CoLocated = 0,

    /// <summary>
    /// The agent runs in a separate broker process that owns the named-pipe connection to the target.
    /// Policy is treated identically to <see cref="CoLocated"/>: the broker (an owned process)
    /// controls redaction and tier choices. MF-11 forced-redaction does NOT apply.
    /// </summary>
    Brokered = 1,

    /// <summary>
    /// The agent is injected into a third-party WPF process.
    /// <see cref="SessionPolicy.EnableRedaction"/> is forced to <see langword="true"/> (MF-11)
    /// and <see cref="SessionPolicy.MaxTier"/> is capped at <see cref="InputTier.L0ReadOnly"/> (S7).
    /// </summary>
    Injection = 2,
}
