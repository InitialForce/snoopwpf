namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_select_item_by_scroll — scroll a virtualized list to realize the target item, then select it.
/// </summary>
[McpServerToolType]
public sealed class SelectItemByScrollTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_select_item_by_scroll")]
    [Description(
        "Scroll the ItemsControl identified by nodeId until the item at targetIndex is realized " +
        "(materialized into the visual tree), then select it by setting Selector.SelectedIndex (L0). " +
        "Works for both virtualized and non-virtualized lists. " +
        "Returns StateDeltaDto with success, stateChanged, previousValue, newValue, and " +
        "failureReason/suggestion if the item could not be realized or selected. " +
        "\n\nGuidelines: " +
        "Use this tool when the target item may not yet be in the visual tree due to " +
        "UI virtualization (VirtualizingStackPanel with many items). " +
        "For non-virtualized lists where the item is already realized, prefer " +
        "wpf_select_item_by_index (simpler, no scroll). " +
        "For text or substring matching, use wpf_select_item instead. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "When the item cannot be materialized after the maximum scroll iterations " +
        "(budget exhausted), returns ElementOutsideViewport. " +
        "Multi-selection controls will have their selection replaced (not appended). " +
        "\n\nApplies to: " +
        "ListBox, ListView, ComboBox, and any Selector subclass.")]
    public Task<string> SelectItemByScrollAsync(
        [Description("Node ID of the ItemsControl container.")] string nodeId,
        [Description("Zero-based index of the item to scroll to and select.")] int targetIndex,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SelectItemByScrollAsync(nodeId, targetIndex, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
