namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Describes a single realized item in an <c>ItemsControl</c>,
/// returned by <c>wpf_get_list_items</c> (WS3-06).
/// </summary>
[DataContract]
public sealed class ListItemDto
{
    /// <summary>Zero-based index of the item within the <c>ItemsControl.Items</c> collection.</summary>
    [DataMember(Name = "index")]
    public int Index { get; init; }

    /// <summary>
    /// Opaque node identifier for the item container element (e.g. <c>ListBoxItem</c>).
    /// Null when the container is not yet materialized.
    /// </summary>
    [DataMember(Name = "nodeId")]
    public string? NodeId { get; init; }

    /// <summary>
    /// Human-readable display name derived from the item's <c>ToString()</c>.
    /// Prompt-injection-sanitized before returning.
    /// </summary>
    [DataMember(Name = "displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary><see langword="true"/> when this item is the currently selected item.</summary>
    [DataMember(Name = "isSelected")]
    public bool IsSelected { get; init; }
}
