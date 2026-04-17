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
        "Poll for structural changes to the WPF visual tree since a previous version. " +
        "Returns immediately (non-blocking) with a changeset of added/removed node IDs " +
        "and the current treeVersion to use as the baseline for the next call. " +
        "\n\nUsage pattern: " +
        "1. Call with sinceVersion=0 to get the initial treeVersion. " +
        "2. Perform mutations (wpf_set_property, wpf_execute_command, etc.). " +
        "3. Call again with the previously returned treeVersion to receive the delta. " +
        "\n\nChange kinds: " +
        "\"added\"   — node appeared in the tree after sinceVersion. " +
        "\"removed\" — node was present at sinceVersion but is no longer in the tree. " +
        "\n\nScoping: " +
        "Supply rootLocator to restrict the poll to a subtree (e.g. a specific window). " +
        "Omit rootLocator (null) to poll the entire application tree. " +
        "\n\nNotes: " +
        "This tool does NOT wait for mutations to settle. " +
        "Use wpf_pump_until_idle (M2-11) before polling when you need deterministic results " +
        "after a UI-triggered async operation. " +
        "Hard cap: 5000 nodes per poll to protect against unbounded traversal.")]
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
