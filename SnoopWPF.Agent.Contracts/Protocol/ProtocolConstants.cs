namespace SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Protocol-level constants shared between host and agent.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>Maximum allowed pipe frame size in bytes (10 MiB).</summary>
    public const int MaxFrameSize = 10_485_760;

    /// <summary>Current protocol version. Both sides must agree.</summary>
    /// <remarks>Bumped to 2 in FX-C3: handshake now uses nonce+HMAC instead of plaintext token echo.</remarks>
    public const int ProtocolVersion = 2;

    /// <summary>Maximum time in milliseconds the host waits for the agent handshake response.</summary>
    public const int HandshakeTimeoutMs = 5_000;
}
