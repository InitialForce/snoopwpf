namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A condition used when searching for elements by property value.
/// </summary>
[DataContract]
public sealed class PropertyConditionDto
{
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator: "Equals" or "Contains".
    /// </summary>
    [DataMember(Name = "operator")]
    public string Operator { get; set; } = string.Empty;

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}
