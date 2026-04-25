namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// MCP tool: <c>wpf_act_sequence</c> — server-side execution of an ordered list of
/// L0/L1 mutation primitives (click / double_click / execute_command / set_text).
/// One tool call replaces N click + set_text + click round-trips for typical form fills.
/// </summary>
[McpServerToolType]
public sealed class ActSequenceTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_act_sequence")]
    [Description(
        "Execute an ordered list of action primitives in a single round-trip. " +
        "Each step is { type, nodeId, value? } where type ∈ { click, double_click, execute_command, set_text }. " +
        "value is required only for set_text. " +
        "\n\nWhen stopOnError=true (default) the sequence aborts at the first step that returns " +
        "StateDelta.Success=false, so subsequent steps don't run against a broken state. When " +
        "stopOnError=false every step runs and the result reports per-step outcome. " +
        "\n\nReturns ActionSequenceResultDto with allSucceeded, stoppedAtIndex (-1 on full success), " +
        "and a Steps list — each entry carries the step's type/nodeId, success flag, full StateDeltaDto, " +
        "and (on failure) errorCode + errorMessage. " +
        "\n\nIntended use: collapse multi-step LLM nav (click → set_text → click → set_text → click) " +
        "into one call, eliminating LLM round-trip overhead. Pair with wpf_get_actionables to plan " +
        "the sequence from a single tree snapshot. " +
        "\n\nMutation must be enabled (EnableMutation=true in SnoopAgentOptions). Each primitive enforces " +
        "its own MaxTier gate; tier failures are reported in the per-step delta.")]
    public Task<string> ActSequenceAsync(
        [Description("Ordered list of action steps. Each step is { type, nodeId, value? }.")] List<ActionStepDto> steps,
        [Description("If true (default), abort at the first failing step. If false, run every step and report per-step outcome.")] bool stopOnError = true,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ExecuteActionSequenceAsync(steps ?? new List<ActionStepDto>(), stopOnError, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
