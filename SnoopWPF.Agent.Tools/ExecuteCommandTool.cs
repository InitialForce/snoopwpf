namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_execute_command — invoke the ICommand bound to a WPF element.
/// </summary>
[McpServerToolType]
public sealed class ExecuteCommandTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_execute_command")]
    [Description(
        "Execute the ICommand bound to a WPF element (e.g. Button.Command). " +
        "Operates at tier L0 — uses the WPF command system directly, no raw Win32 input. " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the command could not be executed. " +
        "\n\nGuidelines: " +
        "Always prefer wpf_execute_command over wpf_click when a Command is bound to the element. " +
        "Use wpf_get_properties to confirm the element has a non-null Command before calling. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "Only resolves the ButtonBase.CommandProperty dependency property; custom command properties " +
        "on non-ButtonBase elements are not supported by this tool. " +
        "CanExecute is checked before Execute — if it returns false the call fails with " +
        "CannotExecuteCommand and a suggestion to inspect the binding. " +
        "CommandParameter is forwarded automatically from ButtonBase.CommandParameterProperty. " +
        "\n\nApplies to: " +
        "Button, RepeatButton, ToggleButton, RadioButton, CheckBox, MenuItem, Hyperlink, " +
        "and any other ButtonBase-derived control with a Command binding.")]
    public async Task<string> ExecuteCommandAsync(
        [Description("Node ID of the element whose Command should be executed.")] string nodeId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.ExecuteCommandAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
