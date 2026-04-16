namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single hit from a wpf_find_elements search.
/// </summary>
[DataContract]
public sealed class FindElementHitDto
{
    /// <summary>Summary of the matched element.</summary>
    [DataMember(Name = "node")]
    public NodeDto Node { get; set; } = new NodeDto();

    /// <summary>Ordered list of display names from the tree root down to this element.</summary>
    [DataMember(Name = "path")]
    public List<string> Path { get; set; } = new List<string>();

    /// <summary><see langword="true"/> when the element has a Command binding (L0 preferred for interaction).</summary>
    [DataMember(Name = "hasCommandBinding")]
    public bool HasCommandBinding { get; init; }
}

/// <summary>
/// Result of wpf_find_elements.
/// </summary>
[DataContract]
public sealed class FindElementResultDto
{
    /// <summary>Matched elements, each with node summary and tree path.</summary>
    [DataMember(Name = "results")]
    public List<FindElementHitDto> Results { get; set; } = new List<FindElementHitDto>();

    /// <summary>Total number of nodes examined during the search.</summary>
    [DataMember(Name = "totalScanned")]
    public int TotalScanned { get; set; }

    /// <summary><see langword="true"/> when the result list was cut at <c>maxResults</c>.</summary>
    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }
}
