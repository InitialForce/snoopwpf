namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_toggle — flip the toggle state of a WPF element via
/// the UI Automation TogglePattern (L1).
/// </summary>
[McpServerToolType]
public sealed class ToggleTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_toggle")]
    [Description(
        "Flip a WPF element's toggle state via the UIA TogglePattern (L1); returns StateDeltaDto. " +
        "Non-deterministic (always flips the current state). CheckBox and RadioButton are rejected with " +
        "PatternNotSupported and a wpf_set_check_state suggestion — use that L0 tool for a specific target " +
        "state. Applies to bare ToggleButton, checkable MenuItem, and any IToggleProvider target; requires " +
        "EnableAutomation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> ToggleAsync(
        [Description("Node ID of the element to toggle.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ToggleAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
