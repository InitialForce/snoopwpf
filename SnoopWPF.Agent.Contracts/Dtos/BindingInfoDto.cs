namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Binding information for a property on a WPF element.
/// </summary>
[DataContract]
public sealed class BindingInfoDto
{
    /// <summary><see langword="true"/> when a binding exists on the requested property.</summary>
    [DataMember(Name = "hasBinding")]
    public bool HasBinding { get; set; }

    /// <summary>CLR type name of the binding (e.g. <c>Binding</c>, <c>MultiBinding</c>, <c>PriorityBinding</c>).</summary>
    [DataMember(Name = "bindingType")]
    public string BindingType { get; set; } = string.Empty;

    /// <summary>Binding path expression (e.g. <c>SelectedItem.Name</c>).</summary>
    [DataMember(Name = "path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>ElementName source of the binding, or empty when not used.</summary>
    [DataMember(Name = "elementName")]
    public string ElementName { get; set; } = string.Empty;

    /// <summary>RelativeSource description (e.g. <c>Self</c>, <c>TemplatedParent</c>), or empty when not used.</summary>
    [DataMember(Name = "relativeSource")]
    public string RelativeSource { get; set; } = string.Empty;

    /// <summary>Binding mode (e.g. <c>OneWay</c>, <c>TwoWay</c>).</summary>
    [DataMember(Name = "mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>When the binding pushes changes back to the source (e.g. <c>PropertyChanged</c>, <c>LostFocus</c>).</summary>
    [DataMember(Name = "updateSourceTrigger")]
    public string UpdateSourceTrigger { get; set; } = string.Empty;

    /// <summary>Short type name of the value converter, or empty when none is set.</summary>
    [DataMember(Name = "converterTypeName")]
    public string ConverterTypeName { get; set; } = string.Empty;

    /// <summary>CLR type name of the binding source object, or empty when unresolvable.</summary>
    [DataMember(Name = "sourceType")]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Overall binding status (e.g. <c>OK</c>, <c>PathError</c>, <c>MissingDataContext</c>).</summary>
    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Error description when the binding is in an error state; null otherwise.</summary>
    [DataMember(Name = "error")]
    public string? Error { get; set; }

    /// <summary><see langword="true"/> when the DataContext is null and the binding depends on it.</summary>
    [DataMember(Name = "dataContextIsNull")]
    public bool DataContextIsNull { get; set; }

    /// <summary>CLR type name of the current DataContext, or empty when null.</summary>
    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;

    /// <summary>String representation of the resolved binding value; null when unresolvable.</summary>
    [DataMember(Name = "resolvedValue")]
    public string? ResolvedValue { get; set; }

    /// <summary>Child bindings for <c>MultiBinding</c> and <c>PriorityBinding</c>; null for simple bindings.</summary>
    [DataMember(Name = "childBindings")]
    public List<BindingInfoDto>? ChildBindings { get; set; }
}
