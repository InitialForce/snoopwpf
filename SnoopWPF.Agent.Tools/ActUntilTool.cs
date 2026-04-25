namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// MCP tool: <c>wpf_act_until</c> — fire one mutation primitive (click / set_text / etc.) and
/// then poll a property predicate on a target node until it matches or the deadline passes.
/// Replaces the "click + wpf_wait_for_property poll loop" round-trip pair.
/// </summary>
[McpServerToolType]
public sealed class ActUntilTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_act_until")]
    [Description(
        "Fire a single action then poll a property predicate server-side until matched or timeout. " +
        "action is { type, nodeId, value? } (same shape as a wpf_act_sequence step). " +
        "predicate is { targetNodeId, propertyName, expectedValue?, presenceExpected } where " +
        "presenceExpected ∈ { present (default), absent }: " +
        "'present' is satisfied when the node resolves AND its property equals expectedValue; " +
        "'absent' is satisfied when the node fails to resolve (e.g. dialog closed). " +
        "\n\nReturns ActUntilResultDto with: actionResult (per-step delta), success (action OK and " +
        "predicate met), predicateMet, timedOut, actualValue (last observed), elapsedMs, pollCount. " +
        "\n\nIntended use: collapse 'click → loop wpf_wait_for_property until X' into a single tool " +
        "call. Eliminates LLM polling overhead and keeps the wait inside the agent process. " +
        "\n\nTimeout is clamped at the MCP request-timeout level (default ~30s); poll interval is 50ms. " +
        "If the action fails, polling is skipped and success=false (no point waiting on a non-effect).")]
    public Task<string> ActUntilAsync(
        [Description("Action step: { type, nodeId, value? }. type ∈ click | double_click | execute_command | set_text. value required for set_text.")] ActionStepDto action,
        [Description("Predicate: { targetNodeId, propertyName, expectedValue?, presenceExpected? }. presenceExpected defaults to \"present\".")] ActUntilPredicateDto predicate,
        [Description("Maximum milliseconds to poll the predicate after the action fires. Default 5000.")] int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ActUntilAsync(action, predicate, timeoutMs, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
