namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Collections.Generic;
using System.Runtime.Serialization;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Sent by the injected agent in response to <see cref="HandshakeChallenge"/>.
/// </summary>
[DataContract]
public sealed class HandshakeResponse
{
    [DataMember(Name = "protocolVersion")]
    public int ProtocolVersion { get; set; }

    [DataMember(Name = "agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    [DataMember(Name = "targetRuntime")]
    public string TargetRuntime { get; set; } = string.Empty;

    [DataMember(Name = "sessionToken")]
    public string SessionToken { get; set; } = string.Empty;

    [DataMember(Name = "dispatchers")]
    public List<DispatcherInfoDto> Dispatchers { get; set; } = new List<DispatcherInfoDto>();

    [DataMember(Name = "capabilities")]
    public List<string> Capabilities { get; set; } = new List<string>();
}
