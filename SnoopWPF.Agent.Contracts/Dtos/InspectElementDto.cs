namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Rich element summary returned by wpf_inspect_element.
/// </summary>
[DataContract]
public sealed class InspectElementDto
{
    /// <summary>Opaque node identifier, stable within a session.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Fully-qualified CLR type name of the element.</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Value of the element's <c>Name</c> / <c>x:Name</c> attribute, or empty when unnamed.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable label combining type and name, used in tool output.</summary>
    [DataMember(Name = "displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Ordered list of display names from the tree root down to this element.</summary>
    [DataMember(Name = "path")]
    public List<string> Path { get; set; } = new List<string>();

    /// <summary>Node ID of the direct parent element, or empty for root nodes.</summary>
    [DataMember(Name = "parentNodeId")]
    public string ParentNodeId { get; set; } = string.Empty;

    /// <summary>Number of direct children in the requested tree type.</summary>
    [DataMember(Name = "childCount")]
    public int ChildCount { get; set; }

    /// <summary>Zero-based depth of this element from the tree root.</summary>
    [DataMember(Name = "depth")]
    public int Depth { get; set; }

    /// <summary>ID of the WPF Dispatcher that owns this element.</summary>
    [DataMember(Name = "dispatcherId")]
    public int DispatcherId { get; set; }

    /// <summary><see langword="true"/> when the element is currently visible on screen.</summary>
    [DataMember(Name = "isVisible")]
    public bool IsVisible { get; set; }

    /// <summary>Actual rendered width of the element in device-independent pixels.</summary>
    [DataMember(Name = "actualWidth")]
    public double ActualWidth { get; set; }

    /// <summary>Actual rendered height of the element in device-independent pixels.</summary>
    [DataMember(Name = "actualHeight")]
    public double ActualHeight { get; set; }

    /// <summary>CLR type name of the element's current DataContext, or empty when null.</summary>
    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when the element has at least one active binding error.</summary>
    [DataMember(Name = "hasBindingErrors")]
    public bool HasBindingErrors { get; set; }

    /// <summary>Number of active binding errors on this element.</summary>
    [DataMember(Name = "bindingErrorCount")]
    public int BindingErrorCount { get; set; }

    /// <summary>Number of triggers defined on this element; null when not yet evaluated.</summary>
    [DataMember(Name = "triggerCount")]
    public int? TriggerCount { get; set; }

    /// <summary>Number of attached behaviors on this element; null when not yet evaluated.</summary>
    [DataMember(Name = "behaviorCount")]
    public int? BehaviorCount { get; set; }
}
