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
    [McpServerTool(Name = "wpf_click")]
    [Description(
        "Invoke the primary click on a WPF element via the UIA InvokePattern (L1); returns StateDeltaDto. " +
        "Prefer wpf_execute_command (L0) when the element has a Command bound (the response includes that " +
        "suggestion). Requires EnableAutomation=true; fails with PatternNotSupported when the element has no " +
        "IInvokeProvider. See docs/mcp-tools-reference.md.")]
    public Task<string> ClickAsync(
        [Description("Node ID of the element to click.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ClickAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
