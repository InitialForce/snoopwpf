namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_get_binding_info — detailed binding diagnostics for a property.
/// </summary>
[McpServerToolType]
public sealed class GetBindingInfoTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_binding_info")]
    [Description("Get detailed data binding information for a specific property on a WPF element. " +
                 "Returns hasBinding, bindingType, path, elementName, relativeSource, mode, updateSourceTrigger, " +
                 "converterTypeName, sourceType, status (Active/PathError/UpdateTargetError/etc.), " +
                 "error, dataContextIsNull, dataContextType, resolvedValue, and childBindings for MultiBindings. " +
                 "Use wpf_get_properties first to identify which properties are data-bound (isDataBound: true).")]
    public async Task<string> GetBindingInfoAsync(
        [Description("Node ID of the element.")] string nodeId,
        [Description("Property name to inspect the binding for (e.g. \"Text\", \"IsEnabled\").")] string propertyName,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.GetBindingInfoAsync(nodeId, propertyName, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
