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
        "Sets the Value of a Slider (or any RangeBase) via SetCurrentValue on " +
        "RangeBase.ValueProperty (L0). No raw Win32 input is used. " +
        "Uses DependencyObject.SetCurrentValue so any TwoWay binding on Value remains intact — " +
        "setting a value does NOT clear the binding chain. " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the value could not be set. " +
        "\n\nGuidelines: " +
        "Use this tool instead of simulated mouse drags whenever the target is a Slider " +
        "and you need a deterministic final value. " +
        "Pass normalized=false (the default) to supply an absolute value; WPF will clamp it " +
        "to [Minimum, Maximum] automatically. " +
        "Pass normalized=true to supply a fraction in [0.0, 1.0] — the engine maps it to " +
        "Minimum + value × (Maximum − Minimum). " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "\n\nLimitations: " +
        "The tool targets RangeBase.ValueProperty only; TickFrequency and IsSnapToTickEnabled " +
        "are respected by WPF's own coerce logic, so the final stored value may differ from " +
        "the requested value when snapping is active. " +
        "DataBinding: if the Value property has a two-way binding, the bound source will be " +
        "updated via the normal DP change notification path. " +
        "\n\nApplies to: " +
        "Slider, ProgressBar, ScrollBar, and any other RangeBase subclass.")]
    public async Task<string> SetSliderValueAsync(
        [Description("Node ID of the Slider element whose value should be set.")] string nodeId,
        [Description("Target value. Clamped to Slider.Minimum..Maximum by WPF unless normalized=true.")] double value,
        [Description(
            "When false (default), value is an absolute number. " +
            "When true, value is a fraction in [0.0, 1.0] mapped to Minimum..Maximum.")] bool normalized = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.SetSliderValueAsync(nodeId, value, normalized, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
