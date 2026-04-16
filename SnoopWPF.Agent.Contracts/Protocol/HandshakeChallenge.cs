namespace SnoopWPF.Agent.Contracts.Protocol;

using System;
using System.Runtime.Serialization;

/// <summary>
/// Sent by the server to the client to initiate the handshake.
/// Contains a random nonce; the token is never transmitted.
/// The client must prove knowledge of the session token by returning
/// <c>HMACSHA256(key: sessionTokenBytes, data: nonce)</c> in <see cref="HandshakeResponse.ProofHmac"/>.
/// </summary>
[DataContract]
public sealed class HandshakeChallenge
{
    /// <summary>
    /// A random 16-byte nonce generated fresh for each connection attempt.
    /// Never reused; prevents replay attacks.
    /// </summary>
    [DataMember(Name = "nonce")]
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    [DataMember(Name = "protocolVersion")]
    public int ProtocolVersion { get; set; }
}
