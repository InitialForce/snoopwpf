namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_set_property — set a WPF element property value at runtime.
/// </summary>
[McpServerToolType]
public sealed class SetPropertyTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_set_property")]
    [Description("Set a WPF element property value at runtime (mutation must be enabled in SnoopAgentOptions). " +
                 "Returns StateDeltaDto with success flag, stateChanged, previousValue, newValue, and failureReason/suggestion if failed. " +
                 "Supported value formats: bool (true/false), numbers, Color (#RRGGBB or named color), " +
                 "Thickness (L,T,R,B or single value), Enum members by name, and strings. " +
                 "Use wpf_get_properties to find writable properties (isReadOnly: false) before calling this tool.")]
    public async Task<string> SetPropertyAsync(
        [Description("Node ID of the element.")] string nodeId,
        [Description("Property name to set (e.g. \"Width\", \"Background\", \"IsEnabled\").")] string propertyName,
        [Description("String representation of the new value. Format depends on property type. Examples: \"200\", \"#FF0000\", \"True\", \"Collapsed\".")] string value,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.SetPropertyAsync(nodeId, propertyName, value, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
