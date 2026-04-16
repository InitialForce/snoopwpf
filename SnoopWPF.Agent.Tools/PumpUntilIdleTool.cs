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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_pump_until_idle")]
    [Description(
        "Wait until the WPF Dispatcher queue AND composition rendering pipeline are simultaneously " +
        "idle (AND-gate, M2-11). Returns immediately when idle; throws DISPATCHER_BUSY if the 5-second " +
        "animation-runaway ceiling is reached. " +
        "\n\nUse before wpf_poll_changes or wpf_wait_for_property when you need deterministic results " +
        "after a UI mutation. " +
        "\n\nNested-pump guard: calling this tool from within an active pump on the same thread " +
        "is rejected with DISPATCHER_BUSY immediately. " +
        "\n\nresources filter: pass an array of resource names to monitor only a subset " +
        "(e.g. [\"Dispatcher\"] to skip CompositionRendering). " +
        "Omit or pass null to monitor all built-in resources (Dispatcher + CompositionRendering).")]
    public async Task<string> PumpUntilIdleAsync(
        [Description("Maximum wait time in milliseconds. Capped at 5000 (animation-runaway ceiling). Default: 5000.")] int timeoutMs = 5000,
        [Description("Optional array of resource names to monitor (e.g. [\"Dispatcher\", \"CompositionRendering\"]). Omit or null to monitor all.")] IReadOnlyList<string>? resources = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.PumpUntilIdleAsync(timeoutMs, resources, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
