namespace SnoopWPF.Agent.Tools;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// MCP tool: wpf_fetch_blob — retrieves a large payload stored in the in-process BlobStore.
/// </summary>
/// <remarks>
/// Blob references (<c>blobRef</c>) are returned by tools that produce large binary payloads
/// (e.g. <c>wpf_capture_screenshot</c>) when the payload would exceed the 64 KB inline limit.
/// The caller invokes this tool with the ref to retrieve the actual bytes.
/// Blobs expire after 5 minutes; a new capture is required after that.
/// </remarks>
[McpServerToolType]
public sealed class FetchBlobTool(BlobStore blobStore)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_fetch_blob")]
    [Description(
        "Fetch a large payload (screenshot, property dump) by its blobRef key. " +
        "blobRef values are returned by other wpf_* tools whose response would exceed 64 KB. " +
        "Blobs expire after 5 minutes; re-run the originating tool to refresh. " +
        "Returns two content blocks: a JSON text block with metadata (key, mimeType, sizeBytes) " +
        "and an inline content block carrying the raw bytes. " +
        "Fails with BLOB_NOT_FOUND if the key is unknown or has expired.")]
    public Task<CallToolResult> FetchBlobAsync(
        [Description("The blobRef key returned by a previous tool call.")] string key,
        CancellationToken ct = default)
    {
        var entry = blobStore.TryGet(key);

        if (entry is null)
        {
            throw ErrorMapping.ToMcpException(
                new SnoopException(SnoopErrorCode.BlobNotFound, $"Blob '{key}' not found or has expired."));
        }

        var metadata = new
        {
            key,
            mimeType = entry.MimeType,
            sizeBytes = entry.Data.Length,
        };

        var metadataJson = JsonSerializer.Serialize(metadata, SerializerOptions);
        var textBlock = new TextContentBlock { Text = metadataJson };

        ContentBlock dataBlock = entry.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? new ImageContentBlock
            {
                Data = new ReadOnlyMemory<byte>(entry.Data),
                MimeType = entry.MimeType,
            }
            : new TextContentBlock
            {
                Text = Encoding.UTF8.GetString(entry.Data),
            };

        return Task.FromResult(new CallToolResult
        {
            Content = new List<ContentBlock> { textBlock, dataBlock },
        });
    }
}
