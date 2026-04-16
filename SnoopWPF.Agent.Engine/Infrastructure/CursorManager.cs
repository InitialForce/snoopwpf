namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Snapshot-based cursor pagination with a configurable TTL.
/// Thread-safe. Snapshots expire after 30 seconds by default (stale flag set).
/// </summary>
public sealed class CursorManager : IDisposable
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CursorEntry> snapshots = new();

    private readonly Func<DateTimeOffset> clock;

    private readonly TimeSpan ttl;

    private readonly Timer sweepTimer;

    private int tokenCounter;

    private bool disposed;

    /// <summary>
    /// Creates a CursorManager.
    /// </summary>
    /// <param name="clock">Time source. Pass a custom function in tests to control time.</param>
    /// <param name="ttl">Snapshot TTL. Defaults to 30s.</param>
    /// <param name="sweepInterval">Background sweep interval. Defaults to 60s.</param>
    public CursorManager(
        Func<DateTimeOffset>? clock = null,
        TimeSpan ttl = default,
        TimeSpan sweepInterval = default)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.ttl = ttl == default ? DefaultTtl : ttl;

        if (sweepInterval == default)
        {
            sweepInterval = TimeSpan.FromSeconds(60);
        }

        this.sweepTimer = new Timer(_ => this.Sweep(), null, sweepInterval, sweepInterval);
    }

    private static void ThrowIfDisposed(bool disposed, object instance)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(instance.GetType().Name);
        }
    }

    /// <summary>
    /// Stores a snapshot of node IDs and returns an opaque cursor token.
    /// </summary>
    public string CreateCursor(IReadOnlyList<string> snapshot)
    {
        ThrowIfDisposed(this.disposed, this);

        var id = Interlocked.Increment(ref this.tokenCounter);
        var token = $"c{id}";
        var entry = new CursorEntry(snapshot, this.clock());
        this.snapshots[token] = entry;
        return token;
    }

    /// <summary>
    /// Returns a page of items from the snapshot identified by cursor.
    /// If cursor is null or not found, returns an empty page.
    /// Stale is set to true when the TTL has expired but data is still served.
    /// </summary>
    public CursorPage GetPage(string? cursor, int pageSize)
    {
        ThrowIfDisposed(this.disposed, this);

        if (cursor is null || !this.snapshots.TryGetValue(cursor, out var entry))
        {
            return new CursorPage(
                items: Array.Empty<string>(),
                nextCursor: null,
                totalCount: 0,
                hasMore: false,
                stale: false);
        }

        var now = this.clock();
        var age = now - entry.CreatedAt;
        var stale = age > this.ttl;

        var offset = entry.Offset;
        var items = entry.Snapshot;
        var end = Math.Min(offset + pageSize, items.Count);
        var page = new string[end - offset];

        for (var i = offset; i < end; i++)
        {
            page[i - offset] = items[i];
        }

        var hasMore = end < items.Count;
        string? nextCursor = null;

        if (hasMore)
        {
            // Advance the offset in the existing entry and return the same cursor token
            entry.Offset = end;
            nextCursor = cursor;
        }
        else
        {
            // All pages consumed; remove the snapshot
            this.snapshots.TryRemove(cursor, out _);
        }

        return new CursorPage(
            items: page,
            nextCursor: nextCursor,
            totalCount: items.Count,
            hasMore: hasMore,
            stale: stale);
    }

    /// <summary>
    /// Removes all snapshots. Used for session cleanup.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed(this.disposed, this);
        this.snapshots.Clear();
    }

    private void Sweep()
    {
        var now = this.clock();
        foreach (var pair in this.snapshots)
        {
            if (now - pair.Value.CreatedAt > this.ttl)
            {
                this.snapshots.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.sweepTimer.Dispose();
        this.snapshots.Clear();
    }

    private sealed class CursorEntry
    {
        public CursorEntry(IReadOnlyList<string> snapshot, DateTimeOffset createdAt)
        {
            this.Snapshot = snapshot;
            this.CreatedAt = createdAt;
            this.Offset = 0;
        }

        public IReadOnlyList<string> Snapshot { get; }

        public DateTimeOffset CreatedAt { get; }

        public int Offset { get; set; }
    }
}

/// <summary>
/// A single page of cursor-paginated results (internal to the engine layer).
/// </summary>
public sealed class CursorPage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CursorPage"/> class.
    /// </summary>
    public CursorPage(
        IReadOnlyList<string> items,
        string? nextCursor,
        int totalCount,
        bool hasMore,
        bool stale)
    {
        this.Items = items;
        this.NextCursor = nextCursor;
        this.TotalCount = totalCount;
        this.HasMore = hasMore;
        this.Stale = stale;
    }

    public IReadOnlyList<string> Items { get; }

    public string? NextCursor { get; }

    public int TotalCount { get; }

    public bool HasMore { get; }

    public bool Stale { get; }
}
