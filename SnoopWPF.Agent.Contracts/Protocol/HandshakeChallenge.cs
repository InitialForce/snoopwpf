namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// Sent by the host to the injected agent to initiate the handshake.
/// </summary>
[DataContract]
public sealed class HandshakeChallenge
{
    [DataMember(Name = "sessionToken")]
    public string SessionToken { get; set; } = string.Empty;

    [DataMember(Name = "protocolVersion")]
    public int ProtocolVersion { get; set; }
}
