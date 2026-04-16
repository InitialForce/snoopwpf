namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Threading;
using NUnit.Framework;
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

    [Test]
    public void DefaultTtl_IsFiveMinutes()
    {
        Assert.That(BlobStore.DefaultTtl, Is.EqualTo(TimeSpan.FromMinutes(5)));
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
}
