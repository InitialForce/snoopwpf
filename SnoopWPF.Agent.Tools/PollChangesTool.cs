namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_poll_changes — non-blocking structural-change detection (M2-10).
/// </summary>
[McpServerToolType]
public sealed class PollChangesTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_poll_changes")]
    [Description(
        "Poll for structural changes to the visual tree since a prior treeVersion (non-blocking); returns " +
        "added/removed node IDs and the new treeVersion. Call with sinceVersion=0 to baseline, then re-call " +
        "with the returned version after mutations (optionally scope to a subtree via rootLocator). Does not " +
        "wait for mutations to settle (call wpf_pump_until_idle first); hard cap 5000 nodes per poll. " +
        "See docs/mcp-tools-reference.md.")]
    public Task<string> PollChangesAsync(
        [Description("Tree version returned by a previous call. Pass 0 on the first call to receive all currently registered nodes as 'added'.")] long sinceVersion = 0,
        [Description("Optional WpfLocator string to scope the poll to a subtree (e.g. \"$type:MainWindow\"). Omit or null for the full application tree.")] string? rootLocator = null,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            WpfLocator? locator = null;
            if (!string.IsNullOrEmpty(rootLocator))
            {
                locator = WpfLocatorParser.Parse(rootLocator);
            }

            var result = await inspector.PollChangesAsync(sinceVersion, locator, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
