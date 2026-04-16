namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of an injection/attach operation.
/// </summary>
[DataContract]
public sealed class AttachResultDto
{
    /// <summary><see langword="true"/> when the agent was successfully injected into the target process.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary>Unique session identifier assigned to this attach operation.</summary>
    [DataMember(Name = "sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Error description when <see cref="Success"/> is <see langword="false"/>; null otherwise.</summary>
    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
