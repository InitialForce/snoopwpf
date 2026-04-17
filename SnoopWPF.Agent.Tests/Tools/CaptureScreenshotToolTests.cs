namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Blob;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="CaptureScreenshotTool"/>.
/// </summary>
[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "NUnit [TearDown] disposes the store after each test.")]
public class CaptureScreenshotToolTests
{
    private FakeSnoopInspector fake = null!;
    private BlobStore blobStore = null!;
    private CaptureScreenshotTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        // Use a very long sweep interval so the timer never fires during tests.
        this.blobStore = new BlobStore(TimeSpan.FromHours(1));
        var options = new SnoopAgentOptions { BlobTtl = TimeSpan.FromMinutes(1) };
        this.tool = new CaptureScreenshotTool(this.fake, this.blobStore, options);
    }

    [TearDown]
    public void TearDown()
    {
        this.blobStore.Dispose();
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsOneTextBlock()
    {
        var fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        this.fake.OnCaptureScreenshot = (nodeId, ct) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto
                {
                    Width = 800,
                    Height = 600,
                    NodeId = "0:1",
                },
                PngBytes = fakePngBytes,
            });

        var result = await this.tool.CaptureScreenshotAsync("0:1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Content, Is.Not.Null);
        Assert.That(result.Content.Count, Is.EqualTo(1));
        Assert.That(result.Content[0], Is.InstanceOf<TextContentBlock>());
    }

    [Test]
    public async Task HappyPath_TextBlock_ContainsMetadataAndBlobRef()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto
                {
                    Width = 1024,
                    Height = 768,
                    NodeId = "0:5",
                },
                PngBytes = new byte[] { 1, 2, 3 },
            });

        var result = await this.tool.CaptureScreenshotAsync("0:5");

        var textBlock = result.Content[0] as TextContentBlock;
        Assert.That(textBlock, Is.Not.Null, "Content block should be TextContentBlock");

        var doc = JsonNode.Parse(textBlock!.Text)!;
        Assert.That(doc["width"]!.GetValue<int>(), Is.EqualTo(1024));
        Assert.That(doc["height"]!.GetValue<int>(), Is.EqualTo(768));
        Assert.That(doc["nodeId"]!.GetValue<string>(), Is.EqualTo("0:5"));
        Assert.That(doc["blobRef"]!.GetValue<string>(), Does.StartWith("blob:screenshot:0:5:"));
        Assert.That(doc["sizeBytes"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(doc["mimeType"]!.GetValue<string>(), Is.EqualTo("image/png"));
    }

    [Test]
    public async Task HappyPath_PngBytes_StoredInBlobStore()
    {
        var fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { Width = 100, Height = 100, NodeId = "0:2" },
                PngBytes = fakePngBytes,
            });

        var result = await this.tool.CaptureScreenshotAsync("0:2");

        // Extract blobRef from the response.
        var textBlock = (TextContentBlock)result.Content[0];
        var doc = JsonNode.Parse(textBlock.Text)!;
        var blobRef = doc["blobRef"]!.GetValue<string>();

        // Verify that the blob exists in the store with the correct bytes.
        var entry = this.blobStore.TryGet(blobRef);
        Assert.That(entry, Is.Not.Null, "BlobStore must contain the captured PNG.");
        Assert.That(entry!.Data, Is.EqualTo(fakePngBytes), "Stored bytes must match the captured PNG.");
        Assert.That(entry.MimeType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task HappyPath_BlobRef_IsFetchable()
    {
        var fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { Width = 50, Height = 50, NodeId = "0:3" },
                PngBytes = fakePngBytes,
            });

        var captureResult = await this.tool.CaptureScreenshotAsync("0:3");

        var textBlock = (TextContentBlock)captureResult.Content[0];
        var doc = JsonNode.Parse(textBlock.Text)!;
        var blobRef = doc["blobRef"]!.GetValue<string>();

        // Round-trip: fetch the blob via FetchBlobTool and verify bytes are identical.
        var fetchTool = new FetchBlobTool(this.blobStore);
        var fetchResult = await fetchTool.FetchBlobAsync(blobRef);

        var imageBlock = fetchResult.Content[1] as ImageContentBlock;
        Assert.That(imageBlock, Is.Not.Null, "wpf_fetch_blob must return an ImageContentBlock for image/png");
        Assert.That(imageBlock!.MimeType, Is.EqualTo("image/png"));
        Assert.That(imageBlock.Data.ToArray(), Is.EqualTo(fakePngBytes));
    }

    [Test]
    public async Task HappyPath_MetadataJson_UsesCamelCaseKeys()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { Width = 10, Height = 20, NodeId = "0:1" },
                PngBytes = new byte[] { 0 },
            });

        var result = await this.tool.CaptureScreenshotAsync();

        var textBlock = result.Content[0] as TextContentBlock;
        Assert.That(textBlock!.Text, Does.Contain("\"nodeId\""));
        Assert.That(textBlock.Text, Does.Contain("\"width\""));
        Assert.That(textBlock.Text, Does.Contain("\"height\""));
        Assert.That(textBlock.Text, Does.Contain("\"blobRef\""));
        Assert.That(textBlock.Text, Does.Not.Contain("\"NodeId\""));
        Assert.That(textBlock.Text, Does.Not.Contain("\"Width\""));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = "not-set";

        this.fake.OnCaptureScreenshot = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { NodeId = "0:7" },
                PngBytes = new byte[] { 0 },
            });
        };

        await this.tool.CaptureScreenshotAsync("0:7");

        Assert.That(capturedNodeId, Is.EqualTo("0:7"));
    }

    [Test]
    public async Task DefaultNodeId_IsNull()
    {
        string? capturedNodeId = "not-null";

        this.fake.OnCaptureScreenshot = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { NodeId = "0:1" },
                PngBytes = new byte[] { 0 },
            });
        };

        await this.tool.CaptureScreenshotAsync();

        Assert.That(capturedNodeId, Is.Null);
    }

    // ── BlobRef uniqueness ──────────────────────────────────────────────────────

    [Test]
    public async Task TwoCaptures_ProduceDifferentBlobRefs()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { NodeId = "0:1" },
                PngBytes = new byte[] { 1 },
            });

        var result1 = await this.tool.CaptureScreenshotAsync();
        var result2 = await this.tool.CaptureScreenshotAsync();

        var ref1 = JsonNode.Parse(((TextContentBlock)result1.Content[0]).Text)!["blobRef"]!.GetValue<string>();
        var ref2 = JsonNode.Parse(((TextContentBlock)result2.Content[0]).Text)!["blobRef"]!.GetValue<string>();

        Assert.That(ref1, Is.Not.EqualTo(ref2), "Each capture must produce a unique blobRef.");
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void ElementNotRenderable_ThrowsMcpException()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            throw new SnoopException(SnoopErrorCode.ElementNotRenderable, "Element has zero size or is not visible");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.CaptureScreenshotAsync("0:5"));

        Assert.That(ex!.Message, Does.Contain("ELEMENT_NOT_RENDERABLE"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.CaptureScreenshotAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.CaptureScreenshotAsync());

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnCaptureScreenshot = (_, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.CaptureScreenshotAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }
}
