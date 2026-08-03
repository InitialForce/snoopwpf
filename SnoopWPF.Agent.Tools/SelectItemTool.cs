namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// MCP tool: wpf_select_item — select an item in an ItemsControl/Selector (L0).
/// A single discriminated tool covering three selection modes: by identifier
/// (text/substring/index-string), by exact index, and by index with scroll-to-realize
/// for virtualized lists.
/// </summary>
[McpServerToolType]
public sealed class SelectItemTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_select_item")]
    [Description(
        "Select an item in a ListBox, ComboBox, ListView, or any Selector control by setting " +
        "SelectedItem/SelectedIndex via SetValue on the dependency property (L0). No raw Win32 " +
        "input is used. Returns StateDeltaDto with success, stateChanged, previousValue, newValue, " +
        "and failureReason/suggestion if the item could not be selected. " +
        "\n\nSelection mode is chosen by which parameter you pass — supply EXACTLY ONE of " +
        "'identifier' or 'index': " +
        "\n(1) 'identifier' — select by zero-based index string (\"0\"), exact item text " +
        "(case-insensitive), or an unambiguous substring. Ambiguous substrings fail with " +
        "LOCATOR_AMBIGUOUS. This mode auto-realizes virtualized items when it resolves a match. " +
        "\n(2) 'index' with scrollToRealize=false (default) — select by exact zero-based index " +
        "without forcing container realization. Use for non-virtualized lists or already-realized items. " +
        "\n(3) 'index' with scrollToRealize=true — scroll the list until the container at 'index' is " +
        "materialized, then select it. Use for virtualized lists (VirtualizingStackPanel) where the " +
        "target container may not yet exist. " +
        "\n\nMutation must be enabled (EnableMutation=true in SnoopAgentOptions). Multi-selection " +
        "controls have their selection replaced (not appended). " +
        "Passing neither or both of 'identifier'/'index' fails with INVALID_ARGUMENT. " +
        "\n\nApplies to: ListBox, ListView, ComboBox, and any Selector subclass.")]
    public Task<string> SelectItemAsync(
        [Description("Node ID of the ItemsControl/Selector whose selection should be changed.")] string nodeId,
        [Description(
            "Identifier-mode selector: zero-based integer index (\"0\"), exact item text, or " +
            "unambiguous substring of item text. Pass this OR 'index', not both.")] string? identifier = null,
        [Description(
            "Index-mode selector: exact zero-based index of the item to select. " +
            "Pass this OR 'identifier', not both.")] int? index = null,
        [Description(
            "Index-mode only: when true, scroll the list to realize a virtualized container at " +
            "'index' before selecting. Ignored in identifier mode.")] bool scrollToRealize = false,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var hasIdentifier = !string.IsNullOrEmpty(identifier);
            var hasIndex = index.HasValue;

            if (hasIdentifier == hasIndex)
            {
                throw new SnoopException(
                    SnoopErrorCode.InvalidArgument,
                    "wpf_select_item requires exactly one of 'identifier' or 'index'. " +
                    "Pass 'identifier' for text/substring/index-string selection, or 'index' " +
                    "(optionally with scrollToRealize=true) for selection by exact index.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.InvalidArgument });
            }

            StateDeltaDto result;
            if (hasIdentifier)
            {
                result = await inspector.SelectItemAsync(nodeId, identifier!, ct).ConfigureAwait(false);
            }
            else if (scrollToRealize)
            {
                result = await inspector.SelectItemByScrollAsync(nodeId, index!.Value, ct).ConfigureAwait(false);
            }
            else
            {
                result = await inspector.SelectItemByIndexAsync(nodeId, index!.Value, ct).ConfigureAwait(false);
            }

            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
