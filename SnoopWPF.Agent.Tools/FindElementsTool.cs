namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// MCP tool: wpf_find_elements — search the visual tree by type name, x:Name, or property conditions.
/// </summary>
[McpServerToolType]
public sealed class FindElementsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_find_elements")]
    [Description("Search the visual tree for elements by type name (substring), x:Name, optional root, and/or " +
                 "property conditions ({property, operator (\"Equals\"|\"Contains\"), value}). Returns a " +
                 "FindElementResultDto with matched nodes and their paths; maxResults default 50, max 200. " +
                 "See docs/mcp-tools-reference.md.")]
    public Task<string> FindElementsAsync(
        [Description("Type name substring to match (case-insensitive, e.g. \"Button\", \"TextBox\"). Omit to match any type.")] string? typeName = null,
        [Description("x:Name attribute to match exactly (case-sensitive). Omit to skip name filtering.")] string? name = null,
        [Description("Node ID of the subtree root to search within. Omit to search the entire visual tree.")] string? rootNodeId = null,
        [Description("Optional list of property conditions to filter by. Each condition: { property, operator (\"Equals\"|\"Contains\"), value }.")] List<PropertyConditionDto>? conditions = null,
        [Description("Tree type: \"visual\" (default), \"logical\", or \"automation\".")] string treeType = "visual",
        [Description("Maximum number of results. Default: 50, max: 200.")] int maxResults = 50,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.FindElementsAsync(typeName, name, rootNodeId, conditions, treeType, maxResults, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
