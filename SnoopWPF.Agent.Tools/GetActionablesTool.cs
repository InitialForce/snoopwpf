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
        "hyperlinks, list items, sliders, expanders, tabs) in the visible tree; each item carries nodeId, " +
        "kind, label, names, enabled state, and an L0 hint (hasCommandBinding → prefer wpf_execute_command). " +
        "Cheaper than wpf_get_visual_tree for deciding what to act on; maxResults default 100, max 200. " +
        "See docs/mcp-tools-reference.md.")]
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
