namespace SnoopWPF.Agent.Tests.Tools;

using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="CaptureScreenshotTool"/>.
/// </summary>
[TestFixture]
public class CaptureScreenshotToolTests
{
    private FakeSnoopInspector fake = null!;

    private CaptureScreenshotTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new CaptureScreenshotTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsTwoContentBlocks()
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
        Assert.That(result.Content.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task HappyPath_FirstBlock_IsTextWithMetadataJson()
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
        Assert.That(textBlock, Is.Not.Null, "First content block should be TextContentBlock");

        var metaDoc = JsonNode.Parse(textBlock!.Text)!;
        Assert.That(metaDoc["width"]!.GetValue<int>(), Is.EqualTo(1024));
        Assert.That(metaDoc["height"]!.GetValue<int>(), Is.EqualTo(768));
        Assert.That(metaDoc["nodeId"]!.GetValue<string>(), Is.EqualTo("0:5"));
    }

    [Test]
    public async Task HappyPath_SecondBlock_IsImageWithPngMimeType()
    {
        var fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        this.fake.OnCaptureScreenshot = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto { Width = 100, Height = 100, NodeId = "0:2" },
                PngBytes = fakePngBytes,
            });

        var result = await this.tool.CaptureScreenshotAsync("0:2");

        var imageBlock = result.Content[1] as ImageContentBlock;
        Assert.That(imageBlock, Is.Not.Null, "Second content block should be ImageContentBlock");
        Assert.That(imageBlock!.MimeType, Is.EqualTo("image/png"));
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
