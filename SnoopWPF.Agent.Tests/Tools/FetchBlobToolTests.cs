namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Blob;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="FetchBlobTool"/>.
/// </summary>
[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "NUnit [TearDown] disposes the store after each test.")]
public sealed class FetchBlobToolTests
{
    private BlobStore store = null!;
    private FetchBlobTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a very long sweep interval so the timer never fires during the test.
        this.store = new BlobStore(TimeSpan.FromHours(1));
        this.tool = new FetchBlobTool(this.store);
    }

    [TearDown]
    public void TearDown()
    {
        this.store.Dispose();
    }

    // ── Happy path — image blob ───────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ImageBlob_ReturnsTwoBlocks()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        this.store.Store("blob:test:1", pngBytes, "image/png");

        var result = await this.tool.FetchBlobAsync("blob:test:1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Content, Is.Not.Null);
        Assert.That(result.Content.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task HappyPath_ImageBlob_FirstBlock_IsMetadataJson()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        this.store.Store("blob:png:42", pngBytes, "image/png");

        var result = await this.tool.FetchBlobAsync("blob:png:42");

        var textBlock = result.Content[0] as TextContentBlock;
        Assert.That(textBlock, Is.Not.Null, "First block must be TextContentBlock");

        var doc = JsonNode.Parse(textBlock!.Text)!;
        Assert.That(doc["key"]!.GetValue<string>(), Is.EqualTo("blob:png:42"));
        Assert.That(doc["mimeType"]!.GetValue<string>(), Is.EqualTo("image/png"));
        Assert.That(doc["sizeBytes"]!.GetValue<int>(), Is.EqualTo(pngBytes.Length));
    }

    [Test]
    public async Task HappyPath_ImageBlob_SecondBlock_IsImageBlock()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        this.store.Store("blob:png:5", pngBytes, "image/png");

        var result = await this.tool.FetchBlobAsync("blob:png:5");

        var imageBlock = result.Content[1] as ImageContentBlock;
        Assert.That(imageBlock, Is.Not.Null, "Second block must be ImageContentBlock for image/ MIME type");
        Assert.That(imageBlock!.MimeType, Is.EqualTo("image/png"));
    }

    // ── Happy path — text blob ────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_TextBlob_SecondBlock_IsTextBlock()
    {
        var textBytes = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        this.store.Store("blob:json:1", textBytes, "application/json");

        var result = await this.tool.FetchBlobAsync("blob:json:1");

        var textBlock = result.Content[1] as TextContentBlock;
        Assert.That(textBlock, Is.Not.Null, "Second block must be TextContentBlock for non-image MIME type");
        Assert.That(textBlock!.Text, Is.EqualTo("{\"hello\":\"world\"}"));
    }

    // ── Metadata JSON casing ──────────────────────────────────────────────────

    [Test]
    public async Task MetadataJson_UsesCamelCaseKeys()
    {
        this.store.Store("blob:x", new byte[] { 1 }, "image/png");

        var result = await this.tool.FetchBlobAsync("blob:x");

        var textBlock = (TextContentBlock)result.Content[0];
        Assert.That(textBlock.Text, Does.Contain("\"key\""));
        Assert.That(textBlock.Text, Does.Contain("\"mimeType\""));
        Assert.That(textBlock.Text, Does.Contain("\"sizeBytes\""));
        Assert.That(textBlock.Text, Does.Not.Contain("\"Key\""));
        Assert.That(textBlock.Text, Does.Not.Contain("\"MimeType\""));
        Assert.That(textBlock.Text, Does.Not.Contain("\"SizeBytes\""));
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Test]
    public void UnknownKey_ThrowsMcpException_WithBlobNotFound()
    {
        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.FetchBlobAsync("blob:does-not-exist"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("BLOB_NOT_FOUND"));
    }

    [Test]
    public void ExpiredBlob_ThrowsMcpException_WithBlobNotFound()
    {
        // Store a blob with a 1 ms TTL so it expires immediately.
        this.store.Store("blob:expired", new byte[] { 0 }, "image/png", TimeSpan.FromMilliseconds(1));

        // Wait long enough for expiry.
        System.Threading.Thread.Sleep(20);

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.FetchBlobAsync("blob:expired"));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("BLOB_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
