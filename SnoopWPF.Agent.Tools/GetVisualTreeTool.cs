namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_visual_tree — depth-limited tree dump.
/// </summary>
[McpServerToolType]
public sealed class GetVisualTreeTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_visual_tree")]
    [Description("Get the visual tree starting from a node. Returns a depth-limited tree with truncation metadata. " +
                 "Nodes at the cut boundary have childrenTruncated: true. Hard cap: 5000 nodes. " +
                 "Use wpf_get_children for cursor-paginated access to large subtrees.")]
    public async Task<string> GetVisualTreeAsync(
        [Description("Node ID to use as root (omit or null to start from app root).")] string? rootNodeId = null,
        [Description("Maximum tree depth to traverse. Default: 3, max: 10.")] int maxDepth = 3,
        [Description("Tree type: \"visual\" (default), \"logical\", or \"automation\".")] string treeType = "visual",
        [Description("Optional list of DependencyProperty names (case-insensitive, max 10) to include inline on each node.")] List<string>? includeProperties = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetVisualTreeAsync(rootNodeId, maxDepth, treeType, includeProperties, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
