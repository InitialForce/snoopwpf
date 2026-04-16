namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// JSON payload written by the broker to the child process's stdin immediately after spawn.
/// Carries the named-pipe name and session token so neither secret appears on the command line.
/// </summary>
/// <remarks>
/// The broker writes exactly one line of JSON to the child's stdin and then closes the stream.
/// The child reads this line during startup when launched with <c>--snoop-pipe</c> but without
/// <c>--snoop-token</c>.
/// </remarks>
[DataContract]
public sealed class BrokerHandshakePayload
{
    /// <summary>Gets or sets the named-pipe name the child should connect to.</summary>
    [DataMember(Name = "pipe")]
    public string Pipe { get; set; } = string.Empty;

    /// <summary>Gets or sets the hex-encoded 256-bit session token.</summary>
    [DataMember(Name = "token")]
    public string Token { get; set; } = string.Empty;
}
