namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_double_click — fire a WPF routed double-click event on an element.
/// </summary>
[McpServerToolType]
public sealed class DoubleClickTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_double_click")]
    [Description(
        "Fire a WPF routed double-click on the element identified by nodeId (L1, WS3-02). " +
        "Primary path: raises MouseLeftButtonDown + MouseLeftButtonUp twice with ClickCount=2 " +
        "on the second pair, then raises Control.MouseDoubleClickEvent. " +
        "Fallback: when the primary path does not set Handled=true and the control type is not " +
        "known to respond to routed double-click (e.g. custom state-machine controls), a " +
        "Win32 SendInput mouse sequence is used instead and a DOUBLE_CLICK_FALLBACK warning " +
        "is emitted in the response. " +
        "Returns StateDeltaDto with success, stateChanged, chosenTier, and warnings[] when " +
        "the fallback path was taken. " +
        "\n\nGuidelines: " +
        "Use this tool for controls that open detail views, start edits, or navigate on " +
        "double-click (e.g. ListBoxItem, TreeViewItem, DataGrid row). " +
        "For controls that respond to single-click, prefer wpf_click (L1) or " +
        "wpf_execute_command (L0) instead. " +
        "Automation must be enabled (EnableAutomation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "Controls relying on mouse-capture state, preview event sequencing, or hit-testing " +
        "may not respond to the routed-event primary path — the fallback handles this. " +
        "Does not check IsEnabled or IsVisible before invoking.")]
    public Task<string> DoubleClickAsync(
        [Description("Node ID of the element to double-click.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.DoubleClickAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
