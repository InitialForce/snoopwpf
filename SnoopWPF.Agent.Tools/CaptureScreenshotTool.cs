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
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// MCP tool: wpf_capture_screenshot — captures a PNG screenshot of an element or window.
/// Stores the raw PNG bytes in the <see cref="BlobStore"/> and returns a reference key
/// (<c>blobRef</c>) rather than inlining the bytes in the response, avoiding LOH pressure
/// for large (up to 67 MB) payloads.  Use <c>wpf_fetch_blob</c> to retrieve the PNG.
/// </summary>
[McpServerToolType]
public sealed class CaptureScreenshotTool(ISnoopInspector inspector, BlobStore blobStore, SnoopAgentOptions agentOptions)
{
    [McpServerTool(Name = "wpf_capture_screenshot")]
    [Description("Capture a PNG screenshot of a WPF element or window; omit nodeId to capture the first visible window. " +
                 "Returns a JSON text block with metadata (width, height, nodeId) and a blobRef key; pass it to " +
                 "wpf_fetch_blob for the PNG bytes. Fails with ELEMENT_NOT_RENDERABLE for a zero-size or hidden element. " +
                 "See docs/mcp-tools-reference.md.")]
    public Task<CallToolResult> CaptureScreenshotAsync(
        [Description("Node ID of the element or window to capture. Omit to capture the first visible window.")] string? nodeId = null,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.WrapCallToolResult(async () =>
        {
            var result = await inspector.CaptureScreenshotAsync(nodeId, ct).ConfigureAwait(false);

            // Store the PNG bytes in the BlobStore under a deterministic key.
            // Key format: "blob:screenshot:{nodeId|window}:{guid-short}" — unique per capture.
            var capturedNodeId = result.Metadata.NodeId;
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var blobKey = $"blob:screenshot:{capturedNodeId}:{uniqueSuffix}";

            blobStore.Store(blobKey, result.PngBytes, "image/png", agentOptions.BlobTtl);

            var responsePayload = new
            {
                width = result.Metadata.Width,
                height = result.Metadata.Height,
                nodeId = capturedNodeId,
                blobRef = blobKey,
                sizeBytes = result.PngBytes.Length,
                mimeType = "image/png",
            };

            var responseJson = JsonSerializer.Serialize(responsePayload, ToolSerializerOptions.Default);
            var textBlock = new TextContentBlock { Text = responseJson };

            return new CallToolResult
            {
                Content = new List<ContentBlock> { textBlock },
            };
        });
    }
}
