namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of wpf_get_visual_tree — the root node with truncation metadata.
/// </summary>
[DataContract]
public sealed class VisualTreeResultDto
{
    [DataMember(Name = "root")]
    public NodeDto Root { get; set; } = new NodeDto();

    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }

    [DataMember(Name = "returnedNodeCount")]
    public int ReturnedNodeCount { get; set; }
}
