namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single property on a WPF element.
/// </summary>
[DataContract]
public sealed class PropertyDto
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;

    [DataMember(Name = "valueSource")]
    public string ValueSource { get; set; } = string.Empty;

    [DataMember(Name = "isLocallySet")]
    public bool IsLocallySet { get; set; }

    [DataMember(Name = "isDataBound")]
    public bool IsDataBound { get; set; }

    [DataMember(Name = "hasBindingError")]
    public bool HasBindingError { get; set; }

    [DataMember(Name = "bindingError")]
    public string? BindingError { get; set; }

    [DataMember(Name = "isReadOnly")]
    public bool IsReadOnly { get; set; }

    [DataMember(Name = "hasTypeConverter")]
    public bool HasTypeConverter { get; set; }

    [DataMember(Name = "isRedacted")]
    public bool IsRedacted { get; set; }
}
