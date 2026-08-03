namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_execute_command — invoke the ICommand bound to a WPF element.
/// </summary>
[McpServerToolType]
public sealed class ExecuteCommandTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_execute_command")]
    [Description(
        "Execute the ICommand bound to a WPF element via ButtonBase.CommandProperty (L0); returns " +
        "StateDeltaDto. Prefer this over wpf_click when a Command is bound; CanExecute is checked first and a " +
        "false result fails with CannotExecuteCommand (CommandParameter is forwarded automatically). Requires " +
        "EnableMutation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> ExecuteCommandAsync(
        [Description("Node ID of the element whose Command should be executed.")] string nodeId,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ExecuteCommandAsync(nodeId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
