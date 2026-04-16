namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_windows — lists top-level WPF windows.
/// </summary>
[McpServerToolType]
public sealed class GetWindowsTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_windows")]
    [Description("List top-level WPF windows. Returns nodeId, title, type name, dimensions, and dispatcherId for each window.")]
    public async Task<string> GetWindowsAsync(
        [Description("Include hidden/invisible windows. Defaults to false.")] bool includeHidden = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetWindowsAsync(includeHidden, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
