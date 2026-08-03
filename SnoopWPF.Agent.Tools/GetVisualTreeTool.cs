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
    [Description("Get a depth-limited visual tree from a node; nodes cut by maxDepth or the 5000-node cap have " +
                 "childrenTruncated=true (use wpf_get_children for those subtrees). See docs/mcp-tools-reference.md.")]
    public Task<string> GetVisualTreeAsync(
        [Description("Node ID to use as root (omit or null to start from app root).")] string? rootNodeId = null,
        [Description("Maximum tree depth to traverse. Default: 3, max: 10.")] int maxDepth = 3,
        [Description("Tree type: \"visual\" (default), \"logical\", or \"automation\".")] string treeType = "visual",
        [Description("Optional list of DependencyProperty names (case-insensitive, max 10) to include inline on each node.")] List<string>? includeProperties = null,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetVisualTreeAsync(rootNodeId, maxDepth, treeType, includeProperties, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
