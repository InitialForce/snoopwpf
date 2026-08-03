namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_children — cursor-paginated direct children of a node.
/// </summary>
[McpServerToolType]
public sealed class GetChildrenTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_children")]
    [Description("Get cursor-paginated direct children of a node (snapshot cursors prevent gaps/duplicates across " +
                 "pages); omit nodeId to return the application root windows (main window first). Pass nextCursor " +
                 "for the next page. See docs/mcp-tools-reference.md.")]
    public Task<string> GetChildrenAsync(
        [Description("Parent node ID (omit for app roots).")] string? nodeId = null,
        [Description("Tree type: \"visual\" (default), \"logical\", or \"automation\".")] string treeType = "visual",
        [Description("Cursor from previous response for pagination (omit for first page).")] string? cursor = null,
        [Description("Number of children to return. Default: 50, max: 200.")] int take = 50,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetChildrenAsync(nodeId, treeType, cursor, take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
