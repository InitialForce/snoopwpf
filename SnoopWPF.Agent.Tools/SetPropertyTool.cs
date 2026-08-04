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
                 "value is a string parsed to the property type (bool, number, Color, Thickness, enum member, string). " +
                 "Returns StateDeltaDto with previousValue, newValue, and failureReason/suggestion on failure. " +
                 "See docs/mcp-tools-reference.md.")]
    public Task<string> SetPropertyAsync(
        [Description("Node ID of the element.")] string nodeId,
        [Description("Property name to set (e.g. \"Width\", \"Background\", \"IsEnabled\").")] string propertyName,
        [Description("String representation of the new value. Format depends on property type. Examples: \"200\", \"#FF0000\", \"True\", \"Collapsed\".")] string value,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SetPropertyAsync(nodeId, propertyName, value, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
