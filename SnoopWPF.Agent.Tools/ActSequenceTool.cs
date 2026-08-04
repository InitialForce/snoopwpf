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
        "Execute an ordered list of action primitives in one round-trip. Each step is { type, nodeId, value? } " +
        "where type ∈ { click, double_click, execute_command, set_text } (value required only for set_text). " +
        "With stopOnError=true (default) the sequence aborts at the first failing step; returns " +
        "ActionSequenceResultDto with per-step outcomes. Requires EnableMutation=true. " +
        "See docs/mcp-tools-reference.md.")]
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
