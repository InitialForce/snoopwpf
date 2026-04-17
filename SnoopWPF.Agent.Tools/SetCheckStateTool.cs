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
        "Set the IsChecked state of a CheckBox or RadioButton via SetValue on the " +
        "ToggleButton.IsCheckedProperty dependency property (L0). No raw Win32 input is used. " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the state could not be set. " +
        "\n\nGuidelines: " +
        "Use this tool instead of simulated clicks whenever the target is a CheckBox or RadioButton " +
        "and you need a deterministic final state. " +
        "For CheckBox, all three states are supported: \"checked\", \"unchecked\", \"indeterminate\". " +
        "Indeterminate requires IsThreeState=true on the CheckBox. " +
        "For RadioButton, only \"checked\" is meaningful — programmatic unchecking from outside " +
        "the group is not supported by WPF; send \"checked\" to select the radio button. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "Bare ToggleButton (not CheckBox or RadioButton) is rejected with PatternNotSupported " +
        "and a suggestion to use wpf_toggle instead. " +
        "DataBinding: if the IsChecked property has a two-way binding, the bound source will be " +
        "updated via the normal DP change notification path. " +
        "\n\nApplies to: " +
        "CheckBox, RadioButton.")]
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
