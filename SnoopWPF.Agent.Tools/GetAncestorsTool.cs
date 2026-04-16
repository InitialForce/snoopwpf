namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_ancestors — ancestor chain from a node to the root.
/// </summary>
[McpServerToolType]
public sealed class GetAncestorsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_ancestors")]
    [Description("Get the ancestor chain from a node up to the root (or up to maxLevels ancestors). " +
                 "Returns a list of AncestorDto objects ordered from immediate parent to root. " +
                 "Each entry includes nodeId, typeName, name, and dataContextType. " +
                 "Use this to understand where an element sits in the visual tree hierarchy.")]
    public async Task<string> GetAncestorsAsync(
        [Description("Node ID of the element whose ancestors to retrieve.")] string nodeId,
        [Description("Maximum number of ancestor levels to return. Omit for all ancestors up to root.")] int? maxLevels = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetAncestorsAsync(nodeId, maxLevels, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
