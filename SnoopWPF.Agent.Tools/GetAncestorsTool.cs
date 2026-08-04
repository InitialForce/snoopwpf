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
    [Description("Get the ancestor chain from a node up to the root (or up to maxLevels ancestors), ordered from " +
                 "immediate parent to root. Each AncestorDto includes nodeId, typeName, name, and dataContextType. " +
                 "See docs/mcp-tools-reference.md.")]
    public Task<string> GetAncestorsAsync(
        [Description("Node ID of the element whose ancestors to retrieve.")] string nodeId,
        [Description("Maximum number of ancestor levels to return. Omit for all ancestors up to root.")] int? maxLevels = null,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetAncestorsAsync(nodeId, maxLevels, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
