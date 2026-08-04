namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_behaviors — list Blend/Microsoft.Xaml.Behaviors behaviors attached to an element.
/// </summary>
[McpServerToolType]
public sealed class GetBehaviorsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_behaviors")]
    [Description("Get all Blend/Microsoft.Xaml.Behaviors behaviors and actions attached to a WPF element " +
                 "(empty list if none); each BehaviorDto has typeName, assemblyName, and name/value properties. " +
                 "See docs/mcp-tools-reference.md.")]
    public Task<string> GetBehaviorsAsync(
        [Description("Node ID of the element whose behaviors to retrieve.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetBehaviorsAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
