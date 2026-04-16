namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_select_item — select an item in a non-virtualized ItemsControl.
/// </summary>
[McpServerToolType]
public sealed class SelectItemTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_select_item")]
    [Description(
        "Select an item in a ListBox, ComboBox, or any Selector control by setting " +
        "SelectedItem/SelectedIndex via SetValue on the dependency property (L0). " +
        "No raw Win32 input is used. " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the item could not be selected. " +
        "\n\nGuidelines: " +
        "Use this tool to select items in non-virtualized ListBox, ComboBox, ListView, or " +
        "any other Selector. " +
        "The 'identifier' parameter accepts three forms: " +
        "(1) Zero-based integer index (e.g. \"0\", \"2\") — selects by position. " +
        "(2) Exact text — the item's ToString() is compared case-insensitively. " +
        "(3) Partial text (substring) — when no exact match exists, an unambiguous " +
        "substring match is used. If two or more items match, the call fails with " +
        "LOCATOR_AMBIGUOUS; use a more specific identifier or an index instead. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "Virtualized lists (VirtualizingStackPanel with many items) are NOT supported by " +
        "this tool — the item container may not be materialized. Use wpf_select_item_scroll " +
        "(M2-04b) for virtualized paths. " +
        "Multi-selection controls (ListBox with SelectionMode=Multiple) will have their " +
        "selection replaced (not appended) by this tool. " +
        "\n\nApplies to: " +
        "ListBox, ListView, ComboBox, and any Selector subclass (non-virtualized).")]
    public async Task<string> SelectItemAsync(
        [Description("Node ID of the ItemsControl whose selection should be changed.")] string nodeId,
        [Description(
            "Item identifier: zero-based integer index (\"0\"), exact item text, " +
            "or unambiguous substring of item text.")] string identifier,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.SelectItemAsync(nodeId, identifier, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
