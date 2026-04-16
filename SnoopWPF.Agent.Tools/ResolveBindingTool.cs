namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_resolve_binding — resolves the full data-binding chain for a property (M2-08).
/// </summary>
[McpServerToolType]
public sealed class ResolveBindingTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_resolve_binding")]
    [Description("Resolve the full data-binding chain for a dependency property on a WPF element. " +
                 "Returns: path (e.g. 'SelectedSession.User.Name'), sourceTypeName, sourceValue, " +
                 "pathSteps (value at each segment), converterTypeName, converterParameter, mode, " +
                 "validationErrors, and status (OK / PathError / ValidationError / MissingDataContext / " +
                 "ConverterError / NoBinding). Use wpf_get_binding_info for a lighter-weight summary " +
                 "or this tool when you need full chain diagnostics.")]
    public async Task<string> ResolveBindingAsync(
        [Description("Node ID of the element (from wpf_get_visual_tree or wpf_find_elements).")] string nodeId,
        [Description("Dependency property name to resolve the binding for (e.g. \"Text\", \"IsEnabled\").")] string propertyName,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.ResolveBindingAsync(nodeId, propertyName, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
