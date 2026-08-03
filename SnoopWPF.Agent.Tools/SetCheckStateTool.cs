namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_set_check_state — set the checked state of a CheckBox or RadioButton.
/// </summary>
[McpServerToolType]
public sealed class SetCheckStateTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_set_check_state")]
    [Description(
        "Set the IsChecked state of a CheckBox or RadioButton via SetCurrentValue on " +
        "ToggleButton.IsCheckedProperty (L0, preserves TwoWay bindings); state is \"checked\", \"unchecked\", " +
        "or \"indeterminate\" (indeterminate requires IsThreeState=true). Returns StateDeltaDto; bare " +
        "ToggleButton is rejected with PatternNotSupported and a wpf_toggle suggestion. Requires " +
        "EnableMutation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> SetCheckStateAsync(
        [Description("Node ID of the element whose check state should be set.")] string nodeId,
        [Description("Target check state: \"checked\", \"unchecked\", or \"indeterminate\".")] string state,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SetCheckStateAsync(nodeId, state, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
