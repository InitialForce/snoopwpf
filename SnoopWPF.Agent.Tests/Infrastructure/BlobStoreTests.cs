namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// Unit tests for <see cref="BlobStore"/>.
/// </summary>
[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "NUnit [TearDown] disposes the store after each test.")]
public sealed class BlobStoreTests
{
    private BlobStore store = null!;

    [SetUp]
    public void SetUp()
    {
        // Disable the sweep timer during tests by using a very long interval.
        this.store = new BlobStore(TimeSpan.FromHours(1));
    }

    [TearDown]
    public void TearDown()
    {
        this.store.Dispose();
    }

    // ── Store + TryGet ────────────────────────────────────────────────────────

    [Test]
    public void StoreAndGet_ReturnsStoredBytes()
    {
        var data = new byte[] { 1, 2, 3 };
        this.store.Store("key1", data, "image/png");

        var entry = this.store.TryGet("key1");

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Data, Is.EqualTo(data));
        Assert.That(entry.MimeType, Is.EqualTo("image/png"));
    }

    [Test]
    public void TryGet_UnknownKey_ReturnsNull()
    {
        var entry = this.store.TryGet("nonexistent");

        Assert.That(entry, Is.Null);
    }

    [Test]
    public void TryGet_OverwrittenKey_ReturnsLatestValue()
    {
        this.store.Store("key", new byte[] { 1 }, "image/png");
        this.store.Store("key", new byte[] { 2, 3 }, "image/jpeg");

        var entry = this.store.TryGet("key");

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Data, Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(entry.MimeType, Is.EqualTo("image/jpeg"));
    }

    // ── TTL / expiry ──────────────────────────────────────────────────────────

    [Test]
    public void TryGet_ExpiredEntry_ReturnsNull()
    {
        this.store.Store("expiring", new byte[] { 9 }, "image/png", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(20); // wait past the TTL

        var entry = this.store.TryGet("expiring");
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void TryGet_NotYetExpired_ReturnsEntry()
    {
        this.store.Store("fresh", new byte[] { 9 }, "image/png", TimeSpan.FromMinutes(10));

        var entry = this.store.TryGet("fresh");
        Assert.That(entry, Is.Not.Null);
    }

    // ── Count ─────────────────────────────────────────────────────────────────

    [Test]
    public void Count_ReflectsStoredEntries()
    {
        Assert.That(this.store.Count, Is.EqualTo(0));

        this.store.Store("a", new byte[] { 1 }, "image/png");
        Assert.That(this.store.Count, Is.EqualTo(1));

        this.store.Store("b", new byte[] { 2 }, "image/jpeg");
        Assert.That(this.store.Count, Is.EqualTo(2));
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Test]
    public void Dispose_ClearsAllEntries()
    {
        this.store.Store("k", new byte[] { 1 }, "image/png");

        this.store.Dispose();

        Assert.That(this.store.Count, Is.EqualTo(0));
    }

    [Test]
    public void DoubleDispose_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            this.store.Dispose();
            this.store.Dispose();
        });
    }

    // ── DefaultTtl ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the static DefaultTtl matches the SnoopAgentOptions default so they cannot
    /// diverge silently. Post-FX6 hotfix the correct value is 60 seconds (was 5 minutes).
    /// </summary>
    [Test]
    public void DefaultTtl_MatchesSessionOption()
    {
        Assert.That(BlobStore.DefaultTtl, Is.EqualTo(new SnoopAgentOptions().BlobTtl));
    }

    // ── ExpiresAt is set correctly ─────────────────────────────────────────────

    [Test]
    public void ExpiresAt_IsApproximatelyNowPlusTtl()
    {
        var before = DateTimeOffset.UtcNow;
        this.store.Store("timed", new byte[] { 0 }, "image/png", TimeSpan.FromMinutes(5));
        var after = DateTimeOffset.UtcNow;

        var entry = this.store.TryGet("timed");
        Assert.That(entry, Is.Not.Null);

        var expectedMin = before.AddMinutes(5);
        var expectedMax = after.AddMinutes(5);
        Assert.That(entry!.ExpiresAt, Is.GreaterThanOrEqualTo(expectedMin));
        Assert.That(entry.ExpiresAt, Is.LessThanOrEqualTo(expectedMax));
    }

    // ── Count cap LRU eviction (bd-1we.5.1) ──────────────────────────────────

    /// <summary>
    /// When BlobStoreMaxCount is 2 and we store a third entry, the oldest entry (by expiresAt)
    /// is evicted before the new entry is inserted, keeping the count at the cap.
    /// </summary>
    [Test]
    public void MaxCountEvictsOldest()
    {
        using var capped = new BlobStore(TimeSpan.FromHours(1), maxCount: 2, maxBytes: long.MaxValue, onEviction: null);

        // Store two entries with different TTLs so "oldest" is deterministic.
        capped.Store("first", new byte[] { 1 }, "image/png", TimeSpan.FromSeconds(10));
        Thread.Sleep(5); // ensure expiresAt ordering is stable
        capped.Store("second", new byte[] { 2 }, "image/png", TimeSpan.FromSeconds(20));

        Assert.That(capped.Count, Is.EqualTo(2));

        // Storing a third entry must evict "first" (oldest expiresAt).
        capped.Store("third", new byte[] { 3 }, "image/png", TimeSpan.FromSeconds(30));

        Assert.That(capped.Count, Is.EqualTo(2));
        Assert.That(capped.TryGet("first"), Is.Null, "oldest entry should have been evicted");
        Assert.That(capped.TryGet("second"), Is.Not.Null);
        Assert.That(capped.TryGet("third"), Is.Not.Null);
    }

    /// <summary>
    /// When total bytes would exceed BlobStoreMaxBytes, the oldest entry is evicted
    /// until the byte budget fits the new entry.
    /// </summary>
    [Test]
    public void MaxBytesEvictsOldest()
    {
        // Allow 5 bytes max; store 3-byte entry first, then store 4-byte entry.
        using var capped = new BlobStore(TimeSpan.FromHours(1), maxCount: int.MaxValue, maxBytes: 5, onEviction: null);

        capped.Store("first", new byte[] { 1, 2, 3 }, "image/png", TimeSpan.FromSeconds(10));
        Assert.That(capped.TotalBytes, Is.EqualTo(3));

        Thread.Sleep(5); // ensure expiresAt ordering
        // Adding 4-byte entry would make total = 7 > 5, so "first" must be evicted first.
        capped.Store("second", new byte[] { 1, 2, 3, 4 }, "image/png", TimeSpan.FromSeconds(20));

        Assert.That(capped.TryGet("first"), Is.Null, "first entry should have been evicted");
        Assert.That(capped.TryGet("second"), Is.Not.Null);
        Assert.That(capped.TotalBytes, Is.EqualTo(4));
    }

    /// <summary>
    /// When an entry is evicted due to cap pressure, the onEviction callback is invoked
    /// with the evicted key, enabling the caller to emit an audit event.
    /// </summary>
    [Test]
    public void EvictionIsAudited()
    {
        var evictedKeys = new List<string>();

        using var capped = new BlobStore(
            TimeSpan.FromHours(1),
            maxCount: 1,
            maxBytes: long.MaxValue,
            onEviction: key => evictedKeys.Add(key));

        capped.Store("first", new byte[] { 1 }, "image/png", TimeSpan.FromSeconds(10));
        Thread.Sleep(5);
        capped.Store("second", new byte[] { 2 }, "image/png", TimeSpan.FromSeconds(20));

        Assert.That(evictedKeys, Has.Count.EqualTo(1));
        Assert.That(evictedKeys[0], Is.EqualTo("first"));
    }

    // ── Concurrent dispose regression (bd-1we.5.2) ───────────────────────────

    /// <summary>
    /// Regression guard for FX5-4 + FX6 hotfix: 100 threads Store()-ing concurrently while
    /// the main thread calls Dispose() after 50ms must complete within 1 second.
    /// Failure here indicates a deadlock in the dispose path (e.g. Timer.Dispose(WaitHandle)).
    /// </summary>
    [Test]
    public void ConcurrentStoreAndDispose_DoesNotHang()
    {
        using var storeUnderTest = new BlobStore(TimeSpan.FromMilliseconds(10));
        var stopFlag = new CancellationTokenSource();
        var threads = new Thread[100];

        for (var i = 0; i < threads.Length; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                while (!stopFlag.Token.IsCancellationRequested)
                {
                    try
                    {
                        storeUnderTest.Store(
                            $"key-{index}",
                            new byte[] { (byte)(index & 0xFF) },
                            "application/octet-stream");
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected after Dispose() is called.
                        break;
                    }

                    Thread.Sleep(1);
                }
            });
            threads[i].IsBackground = true;
            threads[i].Start();
        }

        Thread.Sleep(50);

        // Signal threads to stop and dispose — must not hang.
        stopFlag.Cancel();
        var sw = Stopwatch.StartNew();
        storeUnderTest.Dispose();
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "Dispose must complete within 1s (no deadlock)");

        foreach (var t in threads)
        {
            t.Join(500);
        }
    }
}
