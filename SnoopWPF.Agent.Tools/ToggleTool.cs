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
        "Flip the toggle state of a WPF element using the UI Automation TogglePattern (L1). " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the toggle could not be performed. " +
        "\n\nGuidelines: " +
        "Use wpf_toggle when the target state is unknown and you simply want to flip the current " +
        "IsChecked state. When a specific final state (checked/unchecked/indeterminate) is required, " +
        "prefer wpf_set_check_state (L0) over wpf_toggle — wpf_toggle is non-deterministic. " +
        "Automation must be enabled (EnableAutomation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "CheckBox and RadioButton are rejected with PatternNotSupported and a wpf_set_check_state " +
        "suggestion — use the deterministic L0 tool for those controls. " +
        "The outcome always flips to the opposite of the current state; two successive calls " +
        "return to the original state. " +
        "Does not check IsEnabled or IsVisible before toggling — verify actionability with " +
        "wpf_inspect_element first if the element may be disabled. " +
        "\n\nApplies to: " +
        "ToggleButton (bare, not CheckBox or RadioButton), " +
        "MenuItem with IsCheckable=true, " +
        "and any UIElement whose AutomationPeer supports IToggleProvider.")]
    public async Task<string> ToggleAsync(
        [Description("Node ID of the element to toggle.")] string nodeId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.ToggleAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
