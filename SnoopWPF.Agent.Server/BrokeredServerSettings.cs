namespace SnoopWPF.Agent.Server;

using System;

/// <summary>
/// Configuration for Mode 2 (warm-attach) brokered pipe SERVER role.
/// Pass to <see cref="SnoopAgent.StartBrokeredServerAsync"/> when the WPF target
/// wants to expose itself for broker-initiated warm attach.
/// </summary>
/// <remarks>
/// In Mode 2 the WPF target owns the <c>NamedPipeServerStream</c>; the broker
/// connects as the pipe client.  Contrast with Mode 1 (cold-launch) where the broker
/// owns the pipe server and the target connects as the client via
/// <see cref="SnoopAgent.StartBrokeredClient"/>.
/// </remarks>
public sealed class BrokeredServerSettings
{
    /// <summary>
    /// Optional pipe name override.  When <see langword="null"/> or empty the pipe name is
    /// generated automatically using the format
    /// <c>motioncatalyst-mcp-&lt;sid&gt;-&lt;pid&gt;-&lt;startTimeTicks&gt;</c>.
    /// </summary>
    public string? PipeName { get; init; }

    /// <summary>
    /// Optional 64-character hex session token (256-bit HMAC key).
    /// When <see langword="null"/> or empty a cryptographically-random token is generated
    /// via <c>RandomNumberGenerator.GetBytes(32)</c>.
    /// </summary>
    public string? SessionToken { get; init; }

    /// <summary>
    /// How long the pipe server will wait for a broker connection before automatically
    /// stopping.  <see langword="null"/> (default) means wait indefinitely until the
    /// <see cref="System.Threading.CancellationToken"/> passed to
    /// <see cref="SnoopAgent.StartBrokeredServerAsync"/> is cancelled or the returned
    /// <see cref="BrokeredServerHandle"/> is disposed.
    /// </summary>
    public TimeSpan? LeaseDuration { get; init; }

    /// <summary>
    /// Optional additional agent options forwarded to the underlying
    /// <see cref="SnoopAgent.StartBrokered"/> session when a broker connects.
    /// </summary>
    public SnoopAgentOptions? AgentOptions { get; init; }
}
