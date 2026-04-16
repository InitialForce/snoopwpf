namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single entry in a poll-changes changeset.
/// </summary>
[DataContract]
public sealed class NodeChangeEntryDto
{
    /// <summary>The node ID that changed.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Change kind: "added", "removed", or "property-mutated".
    /// </summary>
    [DataMember(Name = "changeKind")]
    public string ChangeKind { get; init; } = string.Empty;
}

/// <summary>
/// Result of <c>wpf_poll_changes</c> (M2-10).
/// Returns the changeset observed since <c>sinceVersion</c> and the current
/// <c>treeVersion</c> to use as the baseline for the next call.
/// </summary>
[DataContract]
public sealed class PollChangesResultDto
{
    /// <summary>
    /// The current tree version after this call.
    /// Pass this as <c>sinceVersion</c> on the next poll.
    /// </summary>
    [DataMember(Name = "treeVersion")]
    public long TreeVersion { get; init; }

    /// <summary>
    /// The version supplied by the caller (echo-back for diagnostic convenience).
    /// </summary>
    [DataMember(Name = "sinceVersion")]
    public long SinceVersion { get; init; }

    /// <summary>
    /// Nodes added, removed, or whose properties mutated since <c>sinceVersion</c>.
    /// Empty when no structural change has occurred.
    /// </summary>
    [DataMember(Name = "changes")]
    public List<NodeChangeEntryDto> Changes { get; init; } = new();

    /// <summary>
    /// Total number of changes detected (convenience field; equals <see cref="Changes"/>.Count).
    /// </summary>
    [DataMember(Name = "changeCount")]
    public int ChangeCount { get; init; }
}
