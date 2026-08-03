namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_expand_collapse — expand or collapse a WPF element via
/// the UI Automation ExpandCollapsePattern (L1).
/// </summary>
[McpServerToolType]
public sealed class ExpandCollapseTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_expand_collapse")]
    [Description(
        "Expand or collapse a WPF element via the UIA ExpandCollapsePattern (L1); pass action=\"expand\" or " +
        "\"collapse\" (idempotent). Returns StateDeltaDto; fails with PatternNotSupported when the element has " +
        "no IExpandCollapseProvider. Applies to TreeViewItem, Expander, GroupItem, etc.; requires " +
        "EnableAutomation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> ExpandCollapseAsync(
        [Description("Node ID of the element to expand or collapse.")] string nodeId,
        [Description("Action to perform: \"expand\" or \"collapse\".")] string action,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.ExpandCollapseAsync(nodeId, action, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
