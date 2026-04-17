namespace SnoopWPF.Agent.Engine.Blob;

using System;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// In-memory blob store that keeps large payloads (screenshots, property dumps) by reference key.
/// Each blob has a TTL (default 5 minutes); a background sweep removes expired entries.
/// </summary>
/// <remarks>
/// Tool handlers (e.g. <c>CaptureScreenshotTool</c>) that produce large binary payloads
/// store them here and return only a <c>blobRef</c> string to the MCP caller.
/// The agent then calls <c>wpf_fetch_blob</c> to retrieve the payload on demand,
/// keeping primary tool responses under 64 KB.
/// </remarks>
public sealed class BlobStore : IDisposable
{
    /// <summary>Default TTL applied to blobs when no expiry is specified.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, BlobEntry> entries = new(StringComparer.Ordinal);

    // CA2213 suppressed: Timer is disposed via the Timer.Dispose(WaitHandle) overload,
    // which the CA analyzer does not recognise as a Dispose call.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed via Timer.Dispose(WaitHandle) in Dispose() — CA2213 cannot detect the WaitHandle overload.")]
    private readonly Timer sweepTimer;

    private volatile bool disposed;

    /// <summary>
    /// Initializes a new <see cref="BlobStore"/> with the default 5-minute sweep interval.
    /// </summary>
    public BlobStore()
        : this(DefaultTtl)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="BlobStore"/> with a custom sweep interval.
    /// </summary>
    /// <param name="sweepInterval">How often expired entries are purged.</param>
    public BlobStore(TimeSpan sweepInterval)
    {
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
        this.entries[key] = new BlobEntry(data, mimeType, expiresAt);
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

        // Stop the timer and wait for any in-flight Sweep callback to complete
        // before clearing entries.  Timer.Dispose(WaitHandle) blocks until the
        // callback has returned, eliminating the race between Sweep and Dispose.
        using var timerStopped = new ManualResetEventSlim(false);
        this.sweepTimer.Dispose(timerStopped.WaitHandle);
        timerStopped.Wait();

        // Mark disposed AFTER the timer has fully stopped so Sweep cannot observe
        // a partially-cleared entries dictionary.
        this.disposed = true;
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
