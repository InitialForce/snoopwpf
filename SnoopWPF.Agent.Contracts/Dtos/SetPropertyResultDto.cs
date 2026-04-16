namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of a wpf_set_property operation.
/// </summary>
[DataContract]
public sealed class SetPropertyResultDto
{
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    [DataMember(Name = "previousValue")]
    public string PreviousValue { get; set; } = string.Empty;

    [DataMember(Name = "newValue")]
    public string NewValue { get; set; } = string.Empty;

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
