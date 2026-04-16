namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for screenshot capture against a real WPF application.
/// Exercises <c>CaptureScreenshotAsync</c> through the <see cref="McpTestClient"/>.
///
/// Screenshot tests require a visible, rendered window. If the window is not shown
/// (e.g., headless CI), the tests are skipped gracefully.
/// </summary>
[TestFixture]
public sealed class ScreenshotIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flattens a <see cref="NodeDto"/> tree into a flat list.
    /// </summary>
    private static List<NodeDto> Flatten(NodeDto root)
    {
        var result = new List<NodeDto>();
        var queue = new Queue<NodeDto>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // CaptureScreenshotAsync — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that CaptureScreenshotAsync for the main window returns a result.
    /// If the window has no renderable size (headless), the test is skipped.
    /// </summary>
    [Test]
    public async Task CaptureScreenshot_MainWindow_ReturnsResult()
    {
        ScreenshotResultDto result;

        try
        {
            result = await this.Client.Inspector
                .CaptureScreenshotAsync(nodeId: null, ct: default)
                .ConfigureAwait(false);
        }
        catch (SnoopWPF.Agent.Contracts.SnoopException ex)
            when (ex.Code == SnoopWPF.Agent.Contracts.SnoopErrorCode.ElementNotRenderable)
        {
            Assert.Ignore($"Screenshot not available (headless/no-display): {ex.Message}");
            return;
        }

        Assert.That(result, Is.Not.Null, "CaptureScreenshotAsync must return a non-null result.");
        Assert.That(result.Metadata, Is.Not.Null, "ScreenshotResultDto.Metadata must not be null.");
        Assert.That(result.PngBytes, Is.Not.Null, "PngBytes must not be null.");
    }

    /// <summary>
    /// Verifies that the PNG data returned has a valid PNG signature (header bytes).
    /// </summary>
    [Test]
    public async Task CaptureScreenshot_MainWindow_ReturnsPngData()
    {
        ScreenshotResultDto result;

        try
        {
            result = await this.Client.Inspector
                .CaptureScreenshotAsync(nodeId: null, ct: default)
                .ConfigureAwait(false);
        }
        catch (SnoopWPF.Agent.Contracts.SnoopException ex)
            when (ex.Code == SnoopWPF.Agent.Contracts.SnoopErrorCode.ElementNotRenderable)
        {
            Assert.Ignore($"Screenshot not available (headless/no-display): {ex.Message}");
            return;
        }

        Assert.That(result.PngBytes.Length, Is.GreaterThan(0),
            "PngBytes must contain data.");

        // PNG magic bytes: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
        Assert.That(result.PngBytes.Length, Is.GreaterThanOrEqualTo(8),
            "PNG data must be at least 8 bytes long.");
        Assert.That(result.PngBytes[0], Is.EqualTo(0x89), "PNG byte[0] must be 0x89.");
        Assert.That(result.PngBytes[1], Is.EqualTo((byte)'P'), "PNG byte[1] must be 'P'.");
        Assert.That(result.PngBytes[2], Is.EqualTo((byte)'N'), "PNG byte[2] must be 'N'.");
        Assert.That(result.PngBytes[3], Is.EqualTo((byte)'G'), "PNG byte[3] must be 'G'.");
    }

    /// <summary>
    /// Verifies that the metadata width and height are positive.
    /// </summary>
    [Test]
    public async Task CaptureScreenshot_MainWindow_MetadataHasPositiveDimensions()
    {
        ScreenshotResultDto result;

        try
        {
            result = await this.Client.Inspector
                .CaptureScreenshotAsync(nodeId: null, ct: default)
                .ConfigureAwait(false);
        }
        catch (SnoopWPF.Agent.Contracts.SnoopException ex)
            when (ex.Code == SnoopWPF.Agent.Contracts.SnoopErrorCode.ElementNotRenderable)
        {
            Assert.Ignore($"Screenshot not available (headless/no-display): {ex.Message}");
            return;
        }

        Assert.That(result.Metadata.Width, Is.GreaterThan(0),
            "Screenshot Width must be positive.");
        Assert.That(result.Metadata.Height, Is.GreaterThan(0),
            "Screenshot Height must be positive.");
    }

    /// <summary>
    /// Verifies that capturing a specific node (the window) returns valid PNG data.
    /// </summary>
    [Test]
    public async Task CaptureScreenshot_SpecificWindowNode_ReturnsPngData()
    {
        var windows = await this.Client.GetWindowsAsync(includeHidden: false).ConfigureAwait(false);

        if (windows.Count == 0)
        {
            Assert.Ignore("No visible windows found; test inconclusive.");
            return;
        }

        var windowNodeId = windows[0].NodeId;

        ScreenshotResultDto result;

        try
        {
            result = await this.Client.Inspector
                .CaptureScreenshotAsync(nodeId: windowNodeId, ct: default)
                .ConfigureAwait(false);
        }
        catch (SnoopWPF.Agent.Contracts.SnoopException ex)
            when (ex.Code == SnoopWPF.Agent.Contracts.SnoopErrorCode.ElementNotRenderable)
        {
            Assert.Ignore($"Screenshot not available (headless/no-display): {ex.Message}");
            return;
        }

        Assert.That(result, Is.Not.Null);
        Assert.That(result.PngBytes, Is.Not.Null.And.Not.Empty,
            "PngBytes must contain data for the window node.");
    }

    /// <summary>
    /// Verifies that CaptureScreenshotAsync throws SnoopException for an unknown node ID.
    /// </summary>
    [Test]
    public void CaptureScreenshot_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .CaptureScreenshotAsync("0:99999999", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }

    /// <summary>
    /// Verifies that capturing a Button element (if renderable) returns valid data or skips.
    /// </summary>
    [Test]
    public async Task CaptureScreenshot_ButtonElement_ReturnsPngOrSkips()
    {
        // Find a Button node.
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 10).ConfigureAwait(false);
        var nodes = Flatten(tree.Root);
        var button = nodes.FirstOrDefault(
            n => n.TypeName.Equals("Button", StringComparison.OrdinalIgnoreCase)
              || n.TypeName.EndsWith(".Button", StringComparison.OrdinalIgnoreCase));

        if (button == null)
        {
            Assert.Fail("TestWpfApp should contain a Button");
            return;
        }

        ScreenshotResultDto result;

        try
        {
            result = await this.Client.Inspector
                .CaptureScreenshotAsync(nodeId: button.NodeId, ct: default)
                .ConfigureAwait(false);
        }
        catch (SnoopWPF.Agent.Contracts.SnoopException ex)
            when (ex.Code == SnoopWPF.Agent.Contracts.SnoopErrorCode.ElementNotRenderable)
        {
            // Acceptable — Button may not have been rendered yet (headless).
            Assert.Ignore($"Button not renderable: {ex.Message}");
            return;
        }

        Assert.That(result.PngBytes, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Metadata.Width, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.Metadata.Height, Is.GreaterThanOrEqualTo(0));
    }
}
