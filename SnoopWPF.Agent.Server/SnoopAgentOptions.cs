namespace SnoopWPF.Agent.Server;

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
/// Options controlling how <see cref="SnoopAgent"/> creates and runs the MCP server.
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
    /// The generated (or supplied) token is exposed via <see cref="SnoopAgentHandle.SessionToken"/>
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
    /// Whether sensitive property values are redacted. Default true.
    /// </summary>
    public bool EnableRedaction { get; init; } = true;
}
