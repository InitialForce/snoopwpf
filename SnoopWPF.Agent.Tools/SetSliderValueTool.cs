namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_set_slider_value — set the Value of a Slider or RangeBase element.
/// </summary>
[McpServerToolType]
public sealed class SetSliderValueTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_set_slider_value")]
    [Description(
        "Set the Value of a Slider or any RangeBase via SetCurrentValue on RangeBase.ValueProperty " +
        "(L0, preserves TwoWay bindings); returns StateDeltaDto. With normalized=false (default) value is " +
        "absolute (WPF clamps to [Minimum,Maximum]); with normalized=true it is a fraction in [0,1] mapped " +
        "across the range. Requires EnableMutation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> SetSliderValueAsync(
        [Description("Node ID of the Slider element whose value should be set.")] string nodeId,
        [Description("Target value. Clamped to Slider.Minimum..Maximum by WPF unless normalized=true.")] double value,
        [Description(
            "When false (default), value is an absolute number. " +
            "When true, value is a fraction in [0.0, 1.0] mapped to Minimum..Maximum.")] bool normalized = false,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SetSliderValueAsync(nodeId, value, normalized, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
