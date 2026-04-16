namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Binding information for a property on a WPF element.
/// </summary>
[DataContract]
public sealed class BindingInfoDto
{
    [DataMember(Name = "hasBinding")]
    public bool HasBinding { get; set; }

    [DataMember(Name = "bindingType")]
    public string BindingType { get; set; } = string.Empty;

    [DataMember(Name = "path")]
    public string Path { get; set; } = string.Empty;

    [DataMember(Name = "elementName")]
    public string ElementName { get; set; } = string.Empty;

    [DataMember(Name = "relativeSource")]
    public string RelativeSource { get; set; } = string.Empty;

    [DataMember(Name = "mode")]
    public string Mode { get; set; } = string.Empty;

    [DataMember(Name = "updateSourceTrigger")]
    public string UpdateSourceTrigger { get; set; } = string.Empty;

    [DataMember(Name = "converterTypeName")]
    public string ConverterTypeName { get; set; } = string.Empty;

    [DataMember(Name = "sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "error")]
    public string? Error { get; set; }

    [DataMember(Name = "dataContextIsNull")]
    public bool DataContextIsNull { get; set; }

    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;

    [DataMember(Name = "resolvedValue")]
    public string? ResolvedValue { get; set; }

    [DataMember(Name = "childBindings")]
    public List<BindingInfoDto>? ChildBindings { get; set; }
}
