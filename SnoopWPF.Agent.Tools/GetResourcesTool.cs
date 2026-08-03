namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_resources — cursor-paginated resource dictionary entries.
/// </summary>
[McpServerToolType]
public sealed class GetResourcesTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_resources")]
    [Description("Get cursor-paginated resource dictionary entries visible from a node (merged dictionaries included); " +
                 "each entry has key, valueTypeName, valueSummary, origin, and dictionarySource. Filter by resourceKey " +
                 "substring; pass nextCursor for the next page. See docs/mcp-tools-reference.md.")]
    public Task<string> GetResourcesAsync(
        [Description("Node ID to start resource lookup from (resources flow up through merged dictionaries). Omit for application-level resources.")] string? nodeId = null,
        [Description("Case-insensitive substring filter on resource key (optional).")] string? resourceKey = null,
        [Description("Cursor from previous response for pagination (omit for first page).")] string? cursor = null,
        [Description("Number of resources to return. Default: 50, max: 200.")] int take = 50,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetResourcesAsync(nodeId, resourceKey, cursor, take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
