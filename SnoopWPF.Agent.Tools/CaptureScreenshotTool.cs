namespace SnoopWPF.Agent.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_capture_screenshot — captures a PNG screenshot of an element or window.
/// Returns a multi-block MCP response: metadata as text + the PNG as an inline image.
/// </summary>
[McpServerToolType]
public sealed class CaptureScreenshotTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_capture_screenshot")]
    [Description("Capture a PNG screenshot of a WPF element or window. " +
                 "When nodeId is omitted, captures the first visible window. " +
                 "Returns two content blocks: a JSON text block with metadata (width, height, nodeId) " +
                 "and an inline image block (PNG, mimeType: image/png). " +
                 "Fails with ELEMENT_NOT_RENDERABLE if the element has zero size or is not visible; " +
                 "use wpf_get_windows to get a window nodeId for a full window screenshot instead.")]
    public async Task<CallToolResult> CaptureScreenshotAsync(
        [Description("Node ID of the element or window to capture. Omit to capture the first visible window.")] string? nodeId = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.CaptureScreenshotAsync(nodeId, ct).ConfigureAwait(false);

            var metadataJson = JsonSerializer.Serialize(result.Metadata, ToolSerializerOptions.Default);
            var textBlock = new TextContentBlock { Text = metadataJson };

            var imageBlock = new ImageContentBlock
            {
                Data = new ReadOnlyMemory<byte>(result.PngBytes),
                MimeType = "image/png",
            };

            return new CallToolResult
            {
                Content = new List<ContentBlock> { textBlock, imageBlock },
            };
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
