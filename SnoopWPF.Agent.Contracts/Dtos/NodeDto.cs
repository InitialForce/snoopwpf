namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A node returned from tree operations (summary form).
/// </summary>
[DataContract]
public sealed class NodeDto
{
    /// <summary>Opaque identifier for this node, stable within a session.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Fully-qualified CLR type name of the element (e.g. <c>System.Windows.Controls.Button</c>).</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Value of the element's <c>Name</c> / <c>x:Name</c> attribute, or empty when unnamed.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable label combining type and name, used in tool output.</summary>
    [DataMember(Name = "displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Number of direct children in the requested tree type.</summary>
    [DataMember(Name = "childCount")]
    public int ChildCount { get; set; }

    /// <summary><see langword="true"/> when the element has at least one active binding error.</summary>
    [DataMember(Name = "hasBindingError")]
    public bool HasBindingError { get; set; }

    /// <summary>Zero-based depth of this node from the tree root.</summary>
    [DataMember(Name = "depth")]
    public int Depth { get; set; }

    /// <summary><see langword="true"/> when the children list was cut short due to the MaxChildren cap.</summary>
    [DataMember(Name = "childrenTruncated")]
    public bool ChildrenTruncated { get; set; }

    /// <summary>Inline property snapshot when requested via <c>includeProperties</c>; null otherwise.</summary>
    [DataMember(Name = "properties")]
    public List<NameValuePairDto>? Properties { get; set; }

    /// <summary>Inline child nodes when the tree was expanded inline; null in paginated responses.</summary>
    [DataMember(Name = "children")]
    public List<NodeDto>? Children { get; set; }
}
