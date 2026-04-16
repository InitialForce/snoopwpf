namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A condition used when searching for elements by property value.
/// </summary>
[DataContract]
public sealed class PropertyConditionDto
{
    /// <summary>Name of the dependency or CLR property to match against (e.g. <c>Background</c>).</summary>
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator: "Equals" or "Contains".
    /// </summary>
    [DataMember(Name = "operator")]
    public string Operator { get; set; } = string.Empty;

    /// <summary>Expected property value to compare against (string representation).</summary>
    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}
