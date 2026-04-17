namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_properties — cursor-paginated properties of an element.
/// </summary>
[McpServerToolType]
public sealed class GetPropertiesTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_properties")]
    [Description("Get cursor-paginated properties of a WPF element. Each property includes name, typeName, value, " +
                 "valueSource, isLocallySet, isDataBound, hasBindingError, bindingError, isReadOnly, hasTypeConverter, isRedacted. " +
                 "Properties are sorted by name for stable pagination. Redacted properties show \"[REDACTED]\" as value.")]
    public Task<string> GetPropertiesAsync(
        [Description("Node ID of the element.")] string nodeId,
        [Description("Case-insensitive substring filter on property name (optional).")] string? filter = null,
        [Description("Property category filter: \"layout\", \"color\", \"font\", \"grid\", or \"all\" (default).")] string? category = null,
        [Description("Include properties at their default values. Default: false.")] bool includeDefaults = false,
        [Description("Cursor from previous response for pagination (omit for first page).")] string? cursor = null,
        [Description("Number of properties to return. Default: 100, max: 200.")] int take = 100,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.GetPropertiesAsync(nodeId, filter, category, includeDefaults, cursor, take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
