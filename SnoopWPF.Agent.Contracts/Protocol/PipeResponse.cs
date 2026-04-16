namespace SnoopWPF.Agent.Contracts.Protocol;

using System.Runtime.Serialization;

/// <summary>
/// An error payload in a <see cref="PipeResponse"/>.
/// </summary>
[DataContract]
public sealed class PipeErrorPayload
{
    [DataMember(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "suggestion")]
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// A response frame sent from the injected agent back to the host.
/// </summary>
[DataContract]
public sealed class PipeResponse
{
    [DataMember(Name = "id")]
    public int Id { get; set; }

    /// <summary>
    /// Raw JSON of the method-specific result. Not escaped. Null on error.
    /// </summary>
    [DataMember(Name = "resultJson")]
    public string? ResultJson { get; set; }

    [DataMember(Name = "error")]
    public PipeErrorPayload? Error { get; set; }
}
