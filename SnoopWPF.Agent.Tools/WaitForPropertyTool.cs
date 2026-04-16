namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_wait_for_property — poll a WPF element property until an expected condition is met.
/// </summary>
[McpServerToolType]
public sealed class WaitForPropertyTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_wait_for_property")]
    [Description(
        "Poll a WPF element property until its value equals expectedValue (presenceExpected=present) " +
        "or until the element disappears (presenceExpected=absent). " +
        "Returns WaitForPropertyResultDto with conditionMet, actualValue, elapsedMs, pollCount. " +
        "On timeout throws DISPATCHER_BUSY with suggestion to call wpf_pump_until_idle first. " +
        "Use presenceExpected=absent to detect modal dismissal or element removal.")]
    public async Task<string> WaitForPropertyAsync(
        [Description("WpfLocator string identifying the element (e.g. \"$name:myButton\" or \"$type:Button\").")] string locator,
        [Description("Property name to observe (e.g. \"IsEnabled\", \"Text\", \"Visibility\").")] string propertyName,
        [Description("Expected property value as a string. Required when presenceExpected=present; ignored when presenceExpected=absent.")] string? expectedValue = null,
        [Description("Timeout in milliseconds before the call fails with DISPATCHER_BUSY. Default: 5000.")] int timeoutMs = 5000,
        [Description("\"present\" (default): wait until propertyName equals expectedValue. \"absent\": wait until the element disappears.")] string presenceExpected = "present",
        CancellationToken ct = default)
    {
        try
        {
            var wpfLocator = WpfLocatorParser.Parse(locator);
            var result = await inspector.WaitForPropertyAsync(wpfLocator, propertyName, expectedValue, timeoutMs, presenceExpected, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
