namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A node returned from tree operations (summary form).
/// </summary>
[DataContract]
public sealed class NodeDto
{
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Name = "childCount")]
    public int ChildCount { get; set; }

    [DataMember(Name = "hasBindingError")]
    public bool HasBindingError { get; set; }

    [DataMember(Name = "depth")]
    public int Depth { get; set; }

    [DataMember(Name = "childrenTruncated")]
    public bool ChildrenTruncated { get; set; }

    [DataMember(Name = "properties")]
    public List<NameValuePairDto>? Properties { get; set; }

    [DataMember(Name = "children")]
    public List<NodeDto>? Children { get; set; }
}
