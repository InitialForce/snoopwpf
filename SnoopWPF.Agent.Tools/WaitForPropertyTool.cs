namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Diagnostics;

/// <summary>
/// MCP tool: wpf_wait_for_property — poll a WPF element property until an expected condition is met.
/// </summary>
[McpServerToolType]
public sealed class WaitForPropertyTool(ISnoopInspector inspector, SnoopAgentOptions agentOptions)
{
    [McpServerTool(Name = "wpf_wait_for_property")]
    [Description(
        "Poll a WPF element (by WpfLocator) until propertyName equals expectedValue (presenceExpected=present) " +
        "or the element disappears (presenceExpected=absent). Returns WaitForPropertyResultDto (conditionMet, " +
        "actualValue, elapsedMs, pollCount); on timeout throws DISPATCHER_BUSY suggesting wpf_pump_until_idle. " +
        "See docs/mcp-tools-reference.md.")]
    public Task<string> WaitForPropertyAsync(
        [Description("WpfLocator string identifying the element (e.g. \"$name:myButton\" or \"$type:Button\").")] string locator,
        [Description("Property name to observe (e.g. \"IsEnabled\", \"Text\", \"Visibility\").")] string propertyName,
        [Description("Expected property value as a string. Required when presenceExpected=present; ignored when presenceExpected=absent.")] string? expectedValue = null,
        [Description("Timeout in milliseconds before the call fails with DISPATCHER_BUSY. Default: 5000. Maximum: MaxWaitForPropertyMs (default 30000).")] int timeoutMs = 5000,
        [Description("\"present\" (default): wait until propertyName equals expectedValue. \"absent\": wait until the element disappears.")] string presenceExpected = "present",
        CancellationToken ct = default)
    {
        // FX6-A1: clamp timeoutMs to configurable ceiling (default 30 000 ms).
        // An unbounded value would hold the concurrency semaphore for the full duration,
        // starving all other tool calls (semaphore starvation DoS).
        var maxMs = agentOptions.MaxWaitForPropertyMs;
        if (timeoutMs > maxMs)
        {
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidArgument,
                $"timeoutMs={timeoutMs} exceeds the session ceiling of {maxMs} ms. " +
                $"Reduce timeoutMs to at most {maxMs}.",
                suggestions: new[] { SnoopSuggestions.InvalidArgument }));
        }

        return ToolExceptionMapper.Wrap(async () =>
        {
            var wpfLocator = WpfLocatorParser.Parse(locator);
            var result = await inspector.WaitForPropertyAsync(wpfLocator, propertyName, expectedValue, timeoutMs, presenceExpected, ct).ConfigureAwait(false);

            if (!result.ConditionMet)
            {
                // Emit a non-fatal warning so the LLM knows the wait timed out without
                // raising an exception.  The response is still returned successfully;
                // the warning surfaces in the JSON payload via AttachWarnings (bd-1a9.20).
                SnoopAgentContext.AddWarning(
                    "CONDITION_NOT_MET",
                    $"wpf_wait_for_property timed out after {result.ElapsedMs} ms " +
                    $"({result.PollCount} polls). " +
                    $"Property '{propertyName}' did not reach expected value within {timeoutMs} ms. " +
                    "Consider calling wpf_pump_until_idle before retrying.");
            }

            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
