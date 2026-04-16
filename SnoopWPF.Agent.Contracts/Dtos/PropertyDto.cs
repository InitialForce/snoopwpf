namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single property on a WPF element.
/// </summary>
[DataContract]
public sealed class PropertyDto
{
    /// <summary>Dependency property name (e.g. <c>Background</c>).</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short CLR type name of the property's value (e.g. <c>SolidColorBrush</c>).</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>String representation of the current property value.</summary>
    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>WPF <c>BaseValueSource</c> describing where the value originates (e.g. <c>Local</c>, <c>Style</c>).</summary>
    [DataMember(Name = "valueSource")]
    public string ValueSource { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when the value was set directly on the element (not inherited or styled).</summary>
    [DataMember(Name = "isLocallySet")]
    public bool IsLocallySet { get; set; }

    /// <summary><see langword="true"/> when a data binding is active on this property.</summary>
    [DataMember(Name = "isDataBound")]
    public bool IsDataBound { get; set; }

    /// <summary><see langword="true"/> when the active binding has produced an error.</summary>
    [DataMember(Name = "hasBindingError")]
    public bool HasBindingError { get; set; }

    /// <summary>Binding error message when <see cref="HasBindingError"/> is <see langword="true"/>; null otherwise.</summary>
    [DataMember(Name = "bindingError")]
    public string? BindingError { get; set; }

    /// <summary><see langword="true"/> when the property cannot be written (read-only DP or CLR property).</summary>
    [DataMember(Name = "isReadOnly")]
    public bool IsReadOnly { get; set; }

    /// <summary><see langword="true"/> when a <c>TypeConverter</c> is registered for the property's type.</summary>
    [DataMember(Name = "hasTypeConverter")]
    public bool HasTypeConverter { get; set; }

    /// <summary><see langword="true"/> when the value was suppressed by the active redaction policy (e.g. <c>[Sensitive]</c>).</summary>
    [DataMember(Name = "isRedacted")]
    public bool IsRedacted { get; set; }
}
