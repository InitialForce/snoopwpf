namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_list_items — enumerate realized item containers in an ItemsControl.
/// </summary>
[McpServerToolType]
public sealed class GetListItemsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_list_items")]
    [Description(
        "Enumerate the realized item containers of an ItemsControl, returning { index, nodeId, displayName, " +
        "isSelected } for each (virtualized items not yet scrolled into view are omitted, appearing as gaps " +
        "in the index sequence). For virtualized lists call wpf_select_item with index + scrollToRealize=true " +
        "first; non-ItemsControl targets fail with INVALID_ARGUMENT. See docs/mcp-tools-reference.md.")]
    public Task<string> GetListItemsAsync(
        [Description("Node ID of the ItemsControl whose realized items should be enumerated.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetListItemsAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
