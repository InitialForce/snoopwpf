namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Snapshot-based cursor pagination with a configurable TTL and optional node-binding.
/// Thread-safe. Snapshots expire after 30 seconds by default (stale flag set).
///
/// FX6-A2: Cursors can be bound to a <c>nodeId</c> at creation time.  When a bound cursor
/// is retrieved with a mismatched <c>nodeId</c>, a <see cref="SnoopException"/> with code
/// <see cref="SnoopErrorCode.CursorMismatch"/> is thrown, preventing cross-node data leaks.
/// The binding is protected by an HMAC-SHA256 tag (truncated to 8 bytes) computed over
/// <c>{token}:{nodeId}</c> using a per-session signing key supplied at construction time.
/// Cursors created without a <c>nodeId</c> are unbound and accepted regardless of the
/// caller-supplied <c>nodeId</c> (backwards-compatible default behaviour).
/// </summary>
public sealed class CursorManager : IDisposable
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CursorEntry> snapshots = new();

    private readonly Func<DateTimeOffset> clock;

    private readonly TimeSpan ttl;

    private readonly Timer sweepTimer;

    // FX6-A2: per-session HMAC signing key. Null means HMAC is disabled (no signing).
    private readonly byte[]? signingKey;

    private int tokenCounter;

    private volatile bool disposed;

    /// <summary>
    /// Creates a CursorManager.
    /// </summary>
    /// <param name="clock">Time source. Pass a custom function in tests to control time.</param>
    /// <param name="ttl">Snapshot TTL. Defaults to 30s.</param>
    /// <param name="sweepInterval">Background sweep interval. Defaults to 60s.</param>
    /// <param name="signingKey">
    /// Optional per-session HMAC-SHA256 signing key for cursor node-binding (FX6-A2).
    /// When non-null, <see cref="CreateCursor"/> accepts an optional <c>nodeId</c> and
    /// embeds a node-binding tag in the cursor token.
    /// When null (the default) node-binding is disabled and all cursors are unbound.
    /// </param>
    public CursorManager(
        Func<DateTimeOffset>? clock = null,
        TimeSpan ttl = default,
        TimeSpan sweepInterval = default,
        byte[]? signingKey = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.ttl = ttl == default ? DefaultTtl : ttl;
        this.signingKey = signingKey;

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
    /// <param name="snapshot">The ordered list of node IDs to paginate.</param>
    /// <param name="nodeId">
    /// Optional node context to bind to the cursor (FX6-A2).  When provided and a
    /// <see cref="signingKey"/> was supplied at construction, the cursor is HMAC-signed
    /// and <see cref="GetPage"/> will reject any call that supplies a different nodeId.
    /// </param>
    public string CreateCursor(IReadOnlyList<string> snapshot, string? nodeId = null)
    {
        ThrowIfDisposed(this.disposed, this);

        var id = Interlocked.Increment(ref this.tokenCounter);

        string token;
        if (nodeId is not null && this.signingKey is not null)
        {
            // Embed the nodeId and a truncated HMAC tag so that the cursor is bound to the
            // specific node context.  Format: "b{id}:{nodeIdB64}:{hmac8B64}"
            var nodeIdBytes = Encoding.UTF8.GetBytes(nodeId);
            var nodeIdB64 = Convert.ToBase64String(nodeIdBytes);
            var rawToken = $"b{id}";
            var tag = this.ComputeTag(rawToken, nodeId);
            var tagB64 = Convert.ToBase64String(tag);
            token = $"{rawToken}:{nodeIdB64}:{tagB64}";
        }
        else
        {
            // Unbound cursor — no node-binding.
            token = $"c{id}";
        }

        var entry = new CursorEntry(snapshot, this.clock(), nodeId);
        this.snapshots[token] = entry;
        return token;
    }

    /// <summary>
    /// Returns a page of items from the snapshot identified by cursor.
    /// If cursor is null or not found, returns an empty page.
    /// Stale is set to true when the TTL has expired but data is still served.
    /// </summary>
    /// <param name="cursor">The opaque cursor token returned by <see cref="CreateCursor"/>.</param>
    /// <param name="pageSize">Number of items per page. Must be greater than zero.</param>
    /// <param name="nodeId">
    /// Optional node context of the caller (FX6-A2).  When provided and the cursor is bound
    /// (created with a different nodeId), a <see cref="SnoopException"/> with code
    /// <see cref="SnoopErrorCode.CursorMismatch"/> is thrown.
    /// </param>
    public CursorPage GetPage(string? cursor, int pageSize, string? nodeId = null)
    {
        ThrowIfDisposed(this.disposed, this);

        // FX2-C9 (EC-C1): pageSize <= 0 is invalid. pageSize=0 would produce an
        // infinite loop at the caller (hasMore=true, offset never advances);
        // pageSize<0 would allocate a negative-sized array (OverflowException).
        // Reject at the public boundary with a clear exception.
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                "pageSize must be greater than zero.");
        }

        if (cursor is null || !this.snapshots.TryGetValue(cursor, out var entry))
        {
            return new CursorPage(
                items: Array.Empty<string>(),
                nextCursor: null,
                totalCount: 0,
                hasMore: false,
                stale: false);
        }

        // FX6-A2: enforce node-binding.  If the cursor is bound to a specific nodeId, the
        // caller MUST supply the same nodeId, otherwise this is a cross-node cursor replay.
        if (entry.BoundNodeId is not null && nodeId is not null
            && !string.Equals(entry.BoundNodeId, nodeId, StringComparison.Ordinal))
        {
            throw new SnoopException(
                SnoopErrorCode.CursorMismatch,
                $"Cursor was issued for nodeId '{entry.BoundNodeId}' but used with nodeId '{nodeId}'. " +
                "Re-fetch the first page without a cursor token.",
                suggestions: new[] { SnoopSuggestions.CursorMismatch });
        }

        // FX6-A2: also validate the HMAC tag for bound cursors to prevent forged tokens.
        if (this.signingKey is not null && cursor.StartsWith("b", StringComparison.Ordinal))
        {
            if (!this.VerifyTag(cursor, entry.BoundNodeId))
            {
                throw new SnoopException(
                    SnoopErrorCode.CursorMismatch,
                    "Cursor HMAC tag is invalid. The cursor token has been tampered with.",
                    suggestions: new[] { SnoopSuggestions.CursorMismatch });
            }
        }

        var now = this.clock();
        var age = now - entry.CreatedAt;
        var stale = age > this.ttl;

        var items = entry.Snapshot;
        var totalCount = items.Count;

        // Atomically claim an exclusive range [claimedOffset, end) using CAS.
        // Only one thread can claim any given range — no two threads ever serve
        // the same item, regardless of how many concurrent callers hold the entry ref.
        int claimedOffset;
        int end;
        do
        {
            claimedOffset = entry.ReadOffset();
            if (claimedOffset >= totalCount)
            {
                // Cursor already fully consumed (by us or another thread).
                return new CursorPage(
                    items: Array.Empty<string>(),
                    nextCursor: null,
                    totalCount: totalCount,
                    hasMore: false,
                    stale: stale);
            }

            // FX2-C9 (EC-M4): clamp before arithmetic to avoid integer overflow
            // when pageSize is near int.MaxValue and claimedOffset > 0.
            int desired = pageSize >= totalCount - claimedOffset
                ? totalCount
                : claimedOffset + pageSize;
            end = Math.Min(desired, totalCount);
        }
        while (!entry.TryAdvance(claimedOffset, end));

        // We exclusively own [claimedOffset, end).  Build the page.
        var page = new string[end - claimedOffset];
        for (var i = claimedOffset; i < end; i++)
        {
            page[i - claimedOffset] = items[i];
        }

        var hasMore = end < totalCount;

        if (!hasMore)
        {
            // All slots consumed — remove from dict so future TryGetValue calls return empty.
            // It is safe for multiple threads to race here; TryRemove is idempotent.
            this.snapshots.TryRemove(cursor, out _);
        }

        string? nextCursor = hasMore ? cursor : null;

        return new CursorPage(
            items: page,
            nextCursor: nextCursor,
            totalCount: totalCount,
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

    /// <summary>
    /// Computes an 8-byte HMAC-SHA256 tag over <c>{rawToken}:{nodeId}</c>.
    /// </summary>
    private byte[] ComputeTag(string rawToken, string nodeId)
    {
        var input = Encoding.UTF8.GetBytes($"{rawToken}:{nodeId}");
#if NET6_0_OR_GREATER
        var fullHash = HMACSHA256.HashData(this.signingKey!, input);
#else
        using var hmac = new HMACSHA256(this.signingKey!);
        var fullHash = hmac.ComputeHash(input);
#endif
        // Truncate to 8 bytes — sufficient for cursor integrity (not replay, epoch handles that).
        var tag = new byte[8];
        Array.Copy(fullHash, tag, 8);
        return tag;
    }

    /// <summary>
    /// Verifies the HMAC tag embedded in a bound cursor token.
    /// Bound token format: "b{id}:{nodeIdB64}:{hmac8B64}"
    /// </summary>
    private bool VerifyTag(string cursor, string? boundNodeId)
    {
        if (boundNodeId is null)
        {
            return true; // unbound cursor, no tag to verify
        }

        // Extract the raw token prefix (everything before the first colon).
        var firstColon = cursor.IndexOf(":", StringComparison.Ordinal);
        if (firstColon < 0)
        {
            return false;
        }

        var rawToken = cursor.Substring(0, firstColon);
        var lastColon = cursor.LastIndexOf(":", StringComparison.Ordinal);
        if (lastColon <= firstColon)
        {
            return false;
        }

        var tagB64 = cursor.Substring(lastColon + 1);
        byte[] claimedTag;
        try
        {
            claimedTag = Convert.FromBase64String(tagB64);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedTag = this.ComputeTag(rawToken, boundNodeId);
        if (claimedTag.Length != expectedTag.Length)
        {
            return false;
        }

        // Constant-time comparison to prevent timing attacks.
#if NET6_0_OR_GREATER
        return CryptographicOperations.FixedTimeEquals(claimedTag, expectedTag);
#else
        // .NET 4.6.2 fallback: manual constant-time compare.
        var diff = 0;
        for (var i = 0; i < claimedTag.Length; i++)
        {
            diff |= claimedTag[i] ^ expectedTag[i];
        }

        return diff == 0;
#endif
    }

    private sealed class CursorEntry
    {
        /// <summary>
        /// The current read offset into <see cref="Snapshot"/>.
        /// Mutated exclusively via <see cref="TryAdvance"/> to ensure atomic,
        /// race-free advancement using compare-and-swap.
        /// </summary>
        private int offset;

        public CursorEntry(IReadOnlyList<string> snapshot, DateTimeOffset createdAt, string? boundNodeId)
        {
            this.Snapshot = snapshot;
            this.CreatedAt = createdAt;
            this.BoundNodeId = boundNodeId;
            this.offset = 0;
        }

        public IReadOnlyList<string> Snapshot { get; }

        public DateTimeOffset CreatedAt { get; }

        /// <summary>FX6-A2: nodeId this cursor is bound to, or null for unbound cursors.</summary>
        public string? BoundNodeId { get; }

        /// <summary>
        /// Reads the current offset with a volatile load.
        /// </summary>
        public int ReadOffset() => Volatile.Read(ref this.offset);

        /// <summary>
        /// Attempts to atomically advance the offset from <paramref name="expected"/> to
        /// <paramref name="desired"/>. Returns <see langword="true"/> if the CAS succeeded
        /// (this thread exclusively owns the range [expected, desired)); returns
        /// <see langword="false"/> if another thread raced and the caller must retry.
        /// </summary>
        public bool TryAdvance(int expected, int desired)
            => Interlocked.CompareExchange(ref this.offset, desired, expected) == expected;
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
