namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_pump_until_idle — blocks until the WPF Dispatcher AND composition pipeline
/// are simultaneously idle, or until a timeout (5s ceiling) fires (M2-11).
/// </summary>
[McpServerToolType]
public sealed class PumpUntilIdleTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_pump_until_idle")]
    [Description(
        "Wait until the WPF Dispatcher queue AND composition rendering pipeline are simultaneously idle " +
        "(AND-gate); returns immediately when idle, throws DISPATCHER_BUSY at the 5-second ceiling. Use before " +
        "wpf_poll_changes or wpf_wait_for_property for deterministic post-mutation results (optionally pass a " +
        "resources subset to monitor). See docs/mcp-tools-reference.md.")]
    public Task<string> PumpUntilIdleAsync(
        [Description("Maximum wait time in milliseconds. Capped at 5000 (animation-runaway ceiling). Default: 5000.")] int timeoutMs = 5000,
        [Description("Optional array of resource names to monitor (e.g. [\"Dispatcher\", \"CompositionRendering\"]). Omit or null to monitor all.")] IReadOnlyList<string>? resources = null,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.PumpUntilIdleAsync(timeoutMs, resources, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
