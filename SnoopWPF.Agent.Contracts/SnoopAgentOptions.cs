namespace SnoopWPF.Agent.Contracts;

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
}
