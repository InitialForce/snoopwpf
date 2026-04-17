namespace SnoopWPF.Agent.Engine.Blob;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

/// <summary>
/// In-memory blob store that keeps large payloads (screenshots, property dumps) by reference key.
/// Each blob has a configurable TTL (session default 60 seconds via <see cref="SnoopWPF.Agent.Contracts.SnoopAgentOptions.BlobTtl"/>);
/// a background sweep removes expired entries.
/// </summary>
/// <remarks>
/// Tool handlers (e.g. <c>CaptureScreenshotTool</c>) that produce large binary payloads
/// store them here and return only a <c>blobRef</c> string to the MCP caller.
/// The agent then calls <c>wpf_fetch_blob</c> to retrieve the payload on demand,
/// keeping primary tool responses under 64 KB.
/// </remarks>
public sealed class BlobStore : IDisposable
{
    /// <summary>
    /// Default TTL applied to blobs when no expiry is specified. Matches
    /// <see cref="SnoopWPF.Agent.Contracts.SnoopAgentOptions.BlobTtl"/> so the
    /// documented session default and the library fallback do not diverge.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Default maximum number of blobs. Matches
    /// <see cref="SnoopWPF.Agent.Contracts.SnoopAgentOptions.BlobStoreMaxCount"/>.
    /// </summary>
    public const int DefaultMaxCount = 64;

    /// <summary>
    /// Default maximum total bytes. Matches
    /// <see cref="SnoopWPF.Agent.Contracts.SnoopAgentOptions.BlobStoreMaxBytes"/>.
    /// </summary>
    public const long DefaultMaxBytes = 128L * 1024 * 1024; // 128 MB

    private readonly ConcurrentDictionary<string, BlobEntry> entries = new(StringComparer.Ordinal);

    private readonly Timer sweepTimer;

    private readonly int maxCount;
    private readonly long maxBytes;

    // Optional callback invoked (evictedKey) when an entry is evicted due to cap pressure.
    private readonly Action<string>? onEviction;

    // Object used as a lock for the eviction-critical section in Store().
    // ConcurrentDictionary operations are individually atomic, but count/byte-cap eviction
    // requires a brief exclusive window to read count, find the oldest, remove it, and add
    // the new entry without racing with another concurrent Store().
    private readonly object storeLock = new object();

    private volatile bool disposed;

    /// <summary>
    /// Initializes a new <see cref="BlobStore"/> with the default sweep interval (matches <see cref="DefaultTtl"/>).
    /// </summary>
    public BlobStore()
        : this(DefaultTtl)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="BlobStore"/> with a custom sweep interval and default caps.
    /// </summary>
    /// <param name="sweepInterval">How often expired entries are purged.</param>
    public BlobStore(TimeSpan sweepInterval)
        : this(sweepInterval, DefaultMaxCount, DefaultMaxBytes, onEviction: null)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="BlobStore"/> with explicit caps and optional eviction callback.
    /// </summary>
    /// <param name="sweepInterval">How often expired entries are purged.</param>
    /// <param name="maxCount">Maximum number of concurrent entries. Must be at least 1.</param>
    /// <param name="maxBytes">Maximum total byte footprint. Must be at least 1.</param>
    /// <param name="onEviction">
    ///   Optional callback invoked with the evicted key whenever an entry is removed due to
    ///   count or byte cap pressure. Use this to emit an audit event.
    /// </param>
    public BlobStore(TimeSpan sweepInterval, int maxCount, long maxBytes, Action<string>? onEviction)
    {
        if (maxCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "maxCount must be at least 1.");
        }

        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "maxBytes must be at least 1.");
        }

        this.maxCount = maxCount;
        this.maxBytes = maxBytes;
        this.onEviction = onEviction;

        this.sweepTimer = new Timer(
            _ => this.Sweep(),
            state: null,
            dueTime: sweepInterval,
            period: sweepInterval);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stores <paramref name="data"/> under <paramref name="key"/> with the default TTL.
    /// If a blob with the same key already exists it is overwritten.
    /// </summary>
    /// <param name="key">Unique reference key for the blob (e.g. <c>"blob:screenshot:0:1"</c>).</param>
    /// <param name="data">Raw bytes to store.</param>
    /// <param name="mimeType">MIME type of the data (e.g. <c>"image/png"</c>).</param>
    public void Store(string key, byte[] data, string mimeType)
        => this.Store(key, data, mimeType, DefaultTtl);

    /// <summary>
    /// Stores <paramref name="data"/> under <paramref name="key"/> with a custom TTL.
    /// Evicts the oldest entry first if count or byte caps would be exceeded.
    /// </summary>
    public void Store(string key, byte[] data, string mimeType, TimeSpan ttl)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (mimeType is null)
        {
            throw new ArgumentNullException(nameof(mimeType));
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var newEntry = new BlobEntry(data, mimeType, expiresAt);

        // Use a short critical section to keep the eviction logic consistent under
        // concurrent Store() calls. The lock is NOT held during the onEviction callback
        // to avoid blocking other threads while audit I/O is in flight.
        string? evictedKey = null;

        lock (this.storeLock)
        {
            // Determine whether the incoming entry replaces an existing one (same key).
            bool replacing = this.entries.TryGetValue(key, out var existing);

            // Running total of bytes already in the store (excluding the slot we're replacing).
            long currentBytes = this.entries.Values.Sum(e => (long)e.Data.Length);
            if (replacing && existing is not null)
            {
                currentBytes -= existing.Data.Length;
            }

            // Evict oldest entries (by expiresAt asc) until both caps are satisfied.
            while (true)
            {
                int currentCount = this.entries.Count - (replacing ? 1 : 0);
                bool countExceeded = currentCount >= this.maxCount;
                bool bytesExceeded = currentBytes + data.Length > this.maxBytes;

                if (!countExceeded && !bytesExceeded)
                {
                    break;
                }

                // Find the entry with the smallest expiresAt (oldest). Exclude the key
                // being replaced (it will be overwritten, not added as a new entry).
                BlobEntry? oldest = null;
                string? oldestKey = null;
                foreach (var kvp in this.entries)
                {
                    if (replacing && kvp.Key == key)
                    {
                        continue;
                    }

                    if (oldest is null || kvp.Value.ExpiresAt < oldest.ExpiresAt)
                    {
                        oldest = kvp.Value;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey is null)
                {
                    // No evictable candidates.
                    break;
                }

                if (this.entries.TryRemove(oldestKey, out var removed))
                {
                    currentBytes -= removed.Data.Length;
                    evictedKey = oldestKey;
                }
            }

            this.entries[key] = newEntry;
        }

        // Fire the eviction callback outside the lock so audit I/O doesn't stall Store().
        if (evictedKey is not null)
        {
            this.onEviction?.Invoke(evictedKey);
        }
    }

    /// <summary>
    /// Attempts to retrieve the blob stored under <paramref name="key"/>.
    /// Returns <see langword="null"/> if the key is unknown or the entry has expired.
    /// </summary>
    /// <param name="key">The blob reference key.</param>
    /// <returns>The <see cref="BlobEntry"/> or <see langword="null"/>.</returns>
    public BlobEntry? TryGet(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (!this.entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
        {
            // Expired — remove and return null.
            this.entries.TryRemove(key, out _);
            return null;
        }

        return entry;
    }

    /// <summary>
    /// Returns the number of currently stored (possibly including expired) entries.
    /// </summary>
    public int Count => this.entries.Count;

    /// <summary>
    /// Returns the total byte size of all currently stored entries.
    /// </summary>
    public long TotalBytes => this.entries.Values.Sum(e => (long)e.Data.Length);

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        // Set disposed FIRST. Any in-flight Sweep will hit the early-return guard at the
        // top of Sweep() before touching this.entries. Timer.Dispose() then prevents future
        // callbacks from being queued. This avoids the ManualResetEventSlim + Timer.Dispose
        // (WaitHandle) pattern which can deadlock the testhost when the kernel event is not
        // signaled synchronously (observed on net8.0-windows testhost).
        this.disposed = true;
        this.sweepTimer.Dispose();
        this.entries.Clear();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Removes all entries whose TTL has elapsed.
    /// Called on the timer thread — must be thread-safe.
    /// </summary>
    private void Sweep()
    {
        // Guard against a callback that fires after Dispose has been called but before
        // Timer.Dispose(WaitHandle) has completed (possible on .NET Framework).
        if (this.disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in this.entries)
        {
            if (now > kvp.Value.ExpiresAt)
            {
                this.entries.TryRemove(kvp.Key, out _);
            }
        }
    }
}

/// <summary>
/// A single entry in the <see cref="BlobStore"/>.
/// </summary>
public sealed class BlobEntry
{
    /// <summary>Initializes a new <see cref="BlobEntry"/>.</summary>
    public BlobEntry(byte[] data, string mimeType, DateTimeOffset expiresAt)
    {
        this.Data = data;
        this.MimeType = mimeType;
        this.ExpiresAt = expiresAt;
    }

    /// <summary>Raw bytes of the stored payload.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "BlobEntry is a value container; defensive copy would waste memory for potentially large payloads.")]
    public byte[] Data { get; }

    /// <summary>MIME type (e.g. <c>"image/png"</c>).</summary>
    public string MimeType { get; }

    /// <summary>UTC time after which this entry is considered expired.</summary>
    public DateTimeOffset ExpiresAt { get; }
}
