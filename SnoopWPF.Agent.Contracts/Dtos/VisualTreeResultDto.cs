namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of wpf_get_visual_tree — the root node with truncation metadata.
/// </summary>
[DataContract]
public sealed class VisualTreeResultDto
{
    /// <summary>Root node of the returned subtree.</summary>
    [DataMember(Name = "root")]
    public NodeDto Root { get; set; } = new NodeDto();

    /// <summary><see langword="true"/> when the tree was cut short because the node count exceeded the depth or MaxChildren limit.</summary>
    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }

    /// <summary>Total number of nodes included in the response (including all inline children).</summary>
    [DataMember(Name = "returnedNodeCount")]
    public int ReturnedNodeCount { get; set; }
}
