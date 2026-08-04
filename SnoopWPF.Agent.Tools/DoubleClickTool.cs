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
        "Fire a WPF routed double-click on an element (L1); returns StateDeltaDto with chosenTier and " +
        "warnings[] (a Win32 SendInput fallback is used, with a DOUBLE_CLICK_FALLBACK warning, when the " +
        "routed-event path is not handled). Use for controls that open or edit on double-click (ListBoxItem, " +
        "TreeViewItem, DataGrid row). Requires EnableAutomation=true. See docs/mcp-tools-reference.md.")]
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
