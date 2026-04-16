namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_click — invoke the primary click action on a WPF element via
/// the UI Automation InvokePattern (L1).
/// </summary>
[McpServerToolType]
public sealed class ClickTool(ISnoopInspector inspector)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_click")]
    [Description(
        "Invoke the primary click action on a WPF element using the UI Automation InvokePattern (L1). " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the click could not be performed. " +
        "\n\nGuidelines: " +
        "Prefer wpf_execute_command (L0) over wpf_click whenever the element has a Command bound " +
        "(ButtonBase.CommandProperty is non-null). Use wpf_get_properties to check for a Command " +
        "binding before calling this tool. " +
        "When wpf_click succeeds on a command-bound element the response includes a " +
        "wpf_execute_command suggestion — use that tool instead on the next interaction. " +
        "Automation must be enabled (EnableAutomation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "Requires the element to expose the IInvokeProvider automation pattern. Controls that do " +
        "not support IInvokeProvider (e.g. plain TextBlock, Image) will fail with PatternNotSupported. " +
        "This tool does not simulate mouse movement or hover events — use raw Win32 simulation " +
        "(L3+) for elements that depend on mouse-enter state. " +
        "Does not check IsEnabled or IsVisible before invoking — verify actionability with " +
        "wpf_inspect_element first if the element may be disabled. " +
        "\n\nApplies to: " +
        "Button, RepeatButton, ToggleButton, RadioButton, CheckBox, MenuItem, Hyperlink, " +
        "and any UIElement whose AutomationPeer supports IInvokeProvider.")]
    public async Task<string> ClickAsync(
        [Description("Node ID of the element to click.")] string nodeId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.ClickAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
