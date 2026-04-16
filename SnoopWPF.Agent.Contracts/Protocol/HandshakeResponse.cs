namespace SnoopWPF.Agent.Contracts.Protocol;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Sent by the client in response to <see cref="HandshakeChallenge"/>.
/// Contains an HMAC proof instead of the raw session token,
/// so the token is never transmitted over the pipe.
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

    /// <summary>
    /// HMAC-SHA256 proof: <c>HMACSHA256(key: sessionTokenBytes, data: nonce)</c>.
    /// Must be exactly 32 bytes. Replaces the old plaintext <c>sessionToken</c> echo.
    /// </summary>
    [DataMember(Name = "proofHmac")]
    public byte[] ProofHmac { get; set; } = Array.Empty<byte>();

    [DataMember(Name = "dispatchers")]
    public List<DispatcherInfoDto> Dispatchers { get; set; } = new List<DispatcherInfoDto>();

    [DataMember(Name = "capabilities")]
    public List<string> Capabilities { get; set; } = new List<string>();
}
