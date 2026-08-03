namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_triggers — list style/template triggers on an element.
/// </summary>
[McpServerToolType]
public sealed class GetTriggersTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_triggers")]
    [Description("Get all triggers on a WPF element (from Style, ControlTemplate, DataTemplate, or the element itself). " +
                 "Each TriggerDto has triggerType, isActive, source (\"Style\"|\"ControlTemplate\"|\"DataTemplate\"|\"Element\"), " +
                 "conditions, and setters. See docs/mcp-tools-reference.md.")]
    public Task<string> GetTriggersAsync(
        [Description("Node ID of the element whose triggers to retrieve.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetTriggersAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
