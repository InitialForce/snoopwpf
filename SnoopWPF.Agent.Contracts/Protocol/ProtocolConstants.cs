namespace SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Protocol-level constants shared between host and agent.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>Maximum allowed pipe frame size in bytes (10 MiB).</summary>
    public const int MaxFrameSize = 10_485_760;

    /// <summary>Current protocol version. Both sides must agree.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Maximum time in milliseconds the host waits for the agent handshake response.</summary>
    public const int HandshakeTimeoutMs = 5_000;
}
