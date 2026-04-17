namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_inspect_element — rich element summary.
/// </summary>
[McpServerToolType]
public sealed class InspectElementTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_inspect_element")]
    [Description("Get a rich summary of a single WPF element: type, name, path from root, parent, dimensions, " +
                 "DataContext type, binding error count, and whether triggers/behaviors are present. " +
                 "Use this after navigating the tree to get full element context before further inspection.")]
    public Task<string> InspectElementAsync(
        [Description("Node ID of the element to inspect.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.InspectElementAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
