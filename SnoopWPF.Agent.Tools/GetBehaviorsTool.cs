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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_get_behaviors")]
    [Description("Get all Blend/Microsoft.Xaml.Behaviors behaviors and actions attached to a WPF element. " +
                 "Each BehaviorDto includes typeName, assemblyName, and a list of properties (name/value pairs). " +
                 "Returns an empty list if no behaviors are attached. " +
                 "Use wpf_inspect_element first to check behaviorCount before calling this tool.")]
    public async Task<string> GetBehaviorsAsync(
        [Description("Node ID of the element whose behaviors to retrieve.")] string nodeId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetBehaviorsAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
