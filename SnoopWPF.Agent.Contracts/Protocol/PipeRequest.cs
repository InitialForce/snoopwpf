namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// A request frame sent from the host to the injected agent over the named pipe.
/// </summary>
[DataContract]
public sealed class PipeRequest
{
    [DataMember(Name = "id")]
    public int Id { get; set; }

    [DataMember(Name = "method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Raw JSON of the method-specific parameters. Not escaped — the transport layer
    /// parses method-specific types from this string.
    /// </summary>
    [DataMember(Name = "paramsJson")]
    public string ParamsJson { get; set; } = string.Empty;
}
