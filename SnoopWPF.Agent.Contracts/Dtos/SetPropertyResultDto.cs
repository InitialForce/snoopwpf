namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of a wpf_set_property operation.
/// </summary>
[DataContract]
public sealed class SetPropertyResultDto
{
    /// <summary><see langword="true"/> when the property was successfully updated.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary>String representation of the property value before the operation.</summary>
    [DataMember(Name = "previousValue")]
    public string PreviousValue { get; set; } = string.Empty;

    /// <summary>String representation of the property value after the operation.</summary>
    [DataMember(Name = "newValue")]
    public string NewValue { get; set; } = string.Empty;

    /// <summary>Error description when <see cref="Success"/> is <see langword="false"/>; null otherwise.</summary>
    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
