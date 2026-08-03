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
        "Enumerate the realized (materialized) item containers in the ItemsControl identified " +
        "by nodeId and return an array of {index, nodeId, displayName, isSelected} for each. " +
        "Only currently realized containers are returned — virtualized items that have not " +
        "yet been scrolled into view are omitted. " +
        "Returns an array of ListItemDto objects. " +
        "\n\nGuidelines: " +
        "Use this tool to inspect list contents, determine which item is selected, or obtain " +
        "nodeIds for individual item containers for follow-up inspection tools. " +
        "For virtualized lists, call wpf_select_item with an index and scrollToRealize=true first " +
        "to force realization of specific items before calling this tool. " +
        "\n\nLimitations: " +
        "Only realized items are included (virtualized items appear as gaps in the index sequence). " +
        "The element must be an ItemsControl (ListBox, ListView, ComboBox, TreeView, etc.). " +
        "Non-ItemsControl elements fail with INVALID_ARGUMENT. " +
        "\n\nApplies to: " +
        "ListBox, ListView, ComboBox, TreeView, DataGrid, and any ItemsControl subclass.")]
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
