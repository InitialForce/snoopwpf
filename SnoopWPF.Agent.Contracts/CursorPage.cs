namespace SnoopWPF.Agent.Contracts;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single page of cursor-paginated results.
/// </summary>
[DataContract]
public sealed class CursorPage<T>
{
    [DataMember(Name = "items")]
    public List<T> Items { get; set; } = new List<T>();

    [DataMember(Name = "nextCursor")]
    public string? NextCursor { get; set; }

    [DataMember(Name = "totalCount")]
    public int TotalCount { get; set; }

    [DataMember(Name = "hasMore")]
    public bool HasMore { get; set; }

    [DataMember(Name = "stale")]
    public bool Stale { get; set; }

    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }
}
