namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: <c>wpf_get_actionables</c> — compact list of "what you can interact with right now".
/// Filters the visual tree to visible, enabled controls that an LLM can act on (buttons, inputs,
/// checkboxes, menu items, hyperlinks, list items, sliders, expanders, tabs).
/// </summary>
[McpServerToolType]
public sealed class GetActionablesTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_actionables")]
    [Description(
        "Compact list of currently-interactable controls (buttons, inputs, checkboxes, menu items, " +
        "hyperlinks, list items, sliders, expanders, tabs) within the visible visual tree. " +
        "Returns each item with nodeId, kind, label, x:Name, AutomationId, type, enabled state, " +
        "and an L0 hint (hasCommandBinding=true → prefer wpf_execute_command over wpf_click). " +
        "\n\nIntended for LLM-driven navigation: use this once per screen to see your action menu, " +
        "then call wpf_click / wpf_set_text_value / wpf_execute_command on the chosen nodeId. " +
        "Cheaper and lower-token than wpf_get_visual_tree when you only need to decide what to click. " +
        "\n\nFilters: skips invisible (Visibility != Visible, IsVisible=false, ActualWidth/Height=0) " +
        "and non-actionable controls (TextBlock, Image, Border, Grid, etc.). " +
        "Hard cap: maxResults (default 100, max 200). Truncated=true when results were cut.")]
    public Task<string> GetActionablesAsync(
        [Description("Node ID of the subtree root to scan within. Omit to scan the entire visual tree from Application.Current.")] string? rootNodeId = null,
        [Description("Maximum number of results. Default: 100, max: 200.")] int maxResults = 100,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetActionablesAsync(rootNodeId, maxResults, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
