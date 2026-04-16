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
    [Description("Get all triggers defined on a WPF element (from Style, ControlTemplate, DataTemplate, or the element itself). " +
                 "Each TriggerDto includes triggerType, isActive, source (\"Style\"|\"ControlTemplate\"|\"DataTemplate\"|\"Element\"), " +
                 "conditions (list of property/value pairs), and setters (list of property/value pairs). " +
                 "Use wpf_inspect_element first to check triggerCount before calling this tool.")]
    public async Task<string> GetTriggersAsync(
        [Description("Node ID of the element whose triggers to retrieve.")] string nodeId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetTriggersAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
