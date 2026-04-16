namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_session_info — returns process/session metadata.
/// </summary>
[McpServerToolType]
public sealed class SessionInfoTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_session_info")]
    [Description("Get session info: process name, PID, .NET version, dispatchers (with window nodeIds), capabilities, and whether mutation is enabled.")]
    public async Task<string> GetSessionInfoAsync(CancellationToken ct)
    {
        try
        {
            var result = await inspector.GetSessionInfoAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
