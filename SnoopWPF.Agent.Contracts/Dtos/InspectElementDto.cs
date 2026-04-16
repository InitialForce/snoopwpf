namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Rich element summary returned by wpf_inspect_element.
/// </summary>
[DataContract]
public sealed class InspectElementDto
{
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Name = "path")]
    public List<string> Path { get; set; } = new List<string>();

    [DataMember(Name = "parentNodeId")]
    public string ParentNodeId { get; set; } = string.Empty;

    [DataMember(Name = "childCount")]
    public int ChildCount { get; set; }

    [DataMember(Name = "depth")]
    public int Depth { get; set; }

    [DataMember(Name = "dispatcherId")]
    public int DispatcherId { get; set; }

    [DataMember(Name = "isVisible")]
    public bool IsVisible { get; set; }

    [DataMember(Name = "actualWidth")]
    public double ActualWidth { get; set; }

    [DataMember(Name = "actualHeight")]
    public double ActualHeight { get; set; }

    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;

    [DataMember(Name = "hasBindingErrors")]
    public bool HasBindingErrors { get; set; }

    [DataMember(Name = "bindingErrorCount")]
    public int BindingErrorCount { get; set; }

    [DataMember(Name = "triggerCount")]
    public int? TriggerCount { get; set; }

    [DataMember(Name = "behaviorCount")]
    public int? BehaviorCount { get; set; }
}
