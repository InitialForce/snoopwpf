namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_select_item_by_index — select an item by zero-based integer index.
/// </summary>
[McpServerToolType]
public sealed class SelectItemByIndexTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_select_item_by_index")]
    [Description(
        "Select an item in a ListBox, ComboBox, or any Selector control by setting " +
        "Selector.SelectedIndex to the given zero-based integer index (L0). " +
        "Does not force virtualized container realization — use wpf_select_item_by_scroll " +
        "when the target item may not yet be materialized. " +
        "Returns StateDeltaDto with success, stateChanged, previousValue, newValue, and " +
        "failureReason/suggestion if the item could not be selected. " +
        "\n\nGuidelines: " +
        "Use this tool for non-virtualized lists or when the item container is already " +
        "realized (e.g. small lists, recently scrolled items). " +
        "For virtualized lists with many items, use wpf_select_item_by_scroll instead " +
        "to ensure the container is materialized before selection. " +
        "For text or substring matching, use wpf_select_item. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "index must be in [0, Items.Count). Out-of-range values fail with INVALID_ARGUMENT. " +
        "Multi-selection controls will have their selection replaced (not appended). " +
        "\n\nApplies to: " +
        "ListBox, ListView, ComboBox, and any Selector subclass.")]
    public Task<string> SelectItemByIndexAsync(
        [Description("Node ID of the ItemsControl container.")] string nodeId,
        [Description("Zero-based index of the item to select.")] int index,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SelectItemByIndexAsync(nodeId, index, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
