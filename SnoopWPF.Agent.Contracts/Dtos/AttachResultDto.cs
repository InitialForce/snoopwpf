namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of an injection/attach operation.
/// </summary>
[DataContract]
public sealed class AttachResultDto
{
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    [DataMember(Name = "sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
