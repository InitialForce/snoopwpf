namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single hit from a wpf_find_elements search.
/// </summary>
[DataContract]
public sealed class FindElementHitDto
{
    [DataMember(Name = "node")]
    public NodeDto Node { get; set; } = new NodeDto();

    [DataMember(Name = "path")]
    public List<string> Path { get; set; } = new List<string>();

    [DataMember(Name = "hasCommandBinding")]
    public bool HasCommandBinding { get; init; }
}

/// <summary>
/// Result of wpf_find_elements.
/// </summary>
[DataContract]
public sealed class FindElementResultDto
{
    [DataMember(Name = "results")]
    public List<FindElementHitDto> Results { get; set; } = new List<FindElementHitDto>();

    [DataMember(Name = "totalScanned")]
    public int TotalScanned { get; set; }

    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }
}
