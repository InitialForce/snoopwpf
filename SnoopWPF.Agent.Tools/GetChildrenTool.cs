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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_get_children")]
    [Description("Get cursor-paginated direct children of a node. Uses snapshot-based cursors to prevent " +
                 "gaps/duplicates when the visual tree changes between pages. " +
                 "When nodeId is omitted, returns the application root windows (main window first). " +
                 "Pass nextCursor from the previous response to get the next page.")]
    public async Task<string> GetChildrenAsync(
        [Description("Parent node ID (omit for app roots).")] string? nodeId = null,
        [Description("Tree type: \"visual\" (default), \"logical\", or \"automation\".")] string treeType = "visual",
        [Description("Cursor from previous response for pagination (omit for first page).")] string? cursor = null,
        [Description("Number of children to return. Default: 50, max: 200.")] int take = 50,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetChildrenAsync(nodeId, treeType, cursor, take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
