namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Text;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// Integration tests for <see cref="BlobStore"/> and blob register/fetch round-trip.
/// These tests exercise the BlobStore directly (no MCP wire protocol needed).
/// </summary>
[TestFixture]
public sealed class FetchBlobIntegrationTests
{
    // ── Register + Fetch round-trip ───────────────────────────────────────────

    /// <summary>
    /// Registers a blob and immediately fetches it — verifies data and MIME type are preserved.
    /// </summary>
    [Test]
    public void RegisterAndFetch_RoundTrip_DataPreserved()
    {
        using var store = new BlobStore(TimeSpan.FromHours(1));

        var payload = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        store.Store("blob:integration:1", payload, "image/png");

        var entry = store.TryGet("blob:integration:1");

        Assert.That(entry, Is.Not.Null, "Stored blob must be retrievable immediately.");
        Assert.That(entry!.Data, Is.EqualTo(payload), "Round-tripped data must be identical.");
        Assert.That(entry.MimeType, Is.EqualTo("image/png"), "MIME type must be preserved.");
    }

    /// <summary>
    /// Registers a text payload (e.g. a property dump) and verifies content is intact.
    /// </summary>
    [Test]
    public void RegisterAndFetch_TextPayload_ContentIntact()
    {
        using var store = new BlobStore(TimeSpan.FromHours(1));

        var json = "{\"properties\":[{\"name\":\"Width\",\"value\":\"100\"}]}";
        var bytes = Encoding.UTF8.GetBytes(json);
        store.Store("blob:properties:dump:1", bytes, "application/json");

        var entry = store.TryGet("blob:properties:dump:1");

        Assert.That(entry, Is.Not.Null);
        Assert.That(Encoding.UTF8.GetString(entry!.Data), Is.EqualTo(json));
    }

    /// <summary>
    /// Verifies that a blob fetched after TTL expiry is not returned.
    /// </summary>
    [Test]
    public void FetchAfterTtlExpiry_ReturnsNull()
    {
        using var store = new BlobStore(TimeSpan.FromHours(1));

        store.Store("blob:ttl-test", new byte[] { 1, 2, 3 }, "image/png", TimeSpan.FromMilliseconds(5));

        System.Threading.Thread.Sleep(30); // wait past TTL

        var entry = store.TryGet("blob:ttl-test");
        Assert.That(entry, Is.Null, "Expired blob must not be returned.");
    }

    /// <summary>
    /// Verifies that overwriting a blob key replaces the previous content.
    /// </summary>
    [Test]
    public void Overwrite_ReplacesEntry()
    {
        using var store = new BlobStore(TimeSpan.FromHours(1));

        store.Store("blob:overwrite", new byte[] { 0xAA }, "image/png");
        store.Store("blob:overwrite", new byte[] { 0xBB, 0xCC }, "image/jpeg");

        var entry = store.TryGet("blob:overwrite");

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Data, Is.EqualTo(new byte[] { 0xBB, 0xCC }));
        Assert.That(entry.MimeType, Is.EqualTo("image/jpeg"));
    }

    /// <summary>
    /// Verifies that multiple independent blobs coexist without interference.
    /// </summary>
    [Test]
    public void MultipleBlobs_Coexist()
    {
        using var store = new BlobStore(TimeSpan.FromHours(1));

        store.Store("blob:a", new byte[] { 1 }, "image/png");
        store.Store("blob:b", new byte[] { 2 }, "image/jpeg");
        store.Store("blob:c", new byte[] { 3 }, "text/plain");

        Assert.That(store.TryGet("blob:a")!.Data[0], Is.EqualTo(1));
        Assert.That(store.TryGet("blob:b")!.Data[0], Is.EqualTo(2));
        Assert.That(store.TryGet("blob:c")!.Data[0], Is.EqualTo(3));
    }
}
