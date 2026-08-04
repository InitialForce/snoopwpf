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
        "Select an item in a ListBox, ComboBox, or any Selector (L0); returns StateDeltaDto. Pass EXACTLY " +
        "ONE of: 'identifier' (zero-based index string, exact text, or unambiguous substring) or 'index' " +
        "(exact zero-based index, optionally with scrollToRealize=true to materialize a virtualized container " +
        "first). Requires EnableMutation=true; neither/both fails with INVALID_ARGUMENT, ambiguous substring " +
        "with LOCATOR_AMBIGUOUS. See docs/mcp-tools-reference.md.")]
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
            "'index' before selecting. Invalid in identifier mode (INVALID_ARGUMENT).")] bool? scrollToRealize = null,
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

            // scrollToRealize is index-mode only; passing it with identifier is a caller error, not a
            // silent no-op (previously the flag was dropped with no explanation on a virtualized list).
            if (hasIdentifier && scrollToRealize.HasValue)
            {
                throw new SnoopException(
                    SnoopErrorCode.InvalidArgument,
                    "scrollToRealize applies only to index mode; do not pass it with 'identifier'.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.InvalidArgument });
            }

            StateDeltaDto result;
            if (hasIdentifier)
            {
                result = await inspector.SelectItemAsync(nodeId, identifier!, ct).ConfigureAwait(false);
            }
            else if (scrollToRealize == true)
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
