namespace SnoopWPF.Agent.Contracts;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single page of cursor-paginated results.
/// </summary>
[DataContract]
public sealed class CursorPage<T>
{
    /// <summary>Items in this page.</summary>
    [DataMember(Name = "items")]
    public List<T> Items { get; set; } = new List<T>();

    /// <summary>Opaque cursor to pass on the next call to retrieve the following page; null when this is the last page.</summary>
    [DataMember(Name = "nextCursor")]
    public string? NextCursor { get; set; }

    /// <summary>Total number of items across all pages (may be an estimate for large collections).</summary>
    [DataMember(Name = "totalCount")]
    public int TotalCount { get; set; }

    /// <summary><see langword="true"/> when additional pages exist beyond this one.</summary>
    [DataMember(Name = "hasMore")]
    public bool HasMore { get; set; }

    /// <summary><see langword="true"/> when the underlying collection changed since the cursor was issued.</summary>
    [DataMember(Name = "stale")]
    public bool Stale { get; set; }

    /// <summary><see langword="true"/> when the page was cut short due to an internal size cap.</summary>
    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }
}
