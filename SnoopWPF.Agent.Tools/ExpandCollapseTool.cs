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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [McpServerTool(Name = "wpf_expand_collapse")]
    [Description(
        "Expand or collapse a WPF element using the UI Automation ExpandCollapsePattern (L1). " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the operation could not be performed. " +
        "\n\nGuidelines: " +
        "Pass action=\"expand\" to expand the element or action=\"collapse\" to collapse it. " +
        "The action is idempotent: expanding an already-expanded element succeeds without error. " +
        "Automation must be enabled (EnableAutomation=true in SnoopAgentOptions). " +
        "After expanding a TreeViewItem, call wpf_get_children to retrieve the newly revealed " +
        "child nodes. " +
        "\n\nLimitations: " +
        "Elements that do not expose IExpandCollapseProvider (e.g. plain Button, TextBox) are " +
        "rejected with PatternNotSupported. " +
        "Does not check IsEnabled or IsVisible before acting — verify actionability with " +
        "wpf_inspect_element first if the element may be disabled. " +
        "Virtualized tree nodes may not be in the visual tree; scroll or realise them first. " +
        "\n\nApplies to: " +
        "TreeViewItem, " +
        "Expander, " +
        "GroupItem (CollectionViewSource groups), " +
        "and any UIElement whose AutomationPeer supports IExpandCollapseProvider.")]
    public async Task<string> ExpandCollapseAsync(
        [Description("Node ID of the element to expand or collapse.")] string nodeId,
        [Description("Action to perform: \"expand\" or \"collapse\".")] string action,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.ExpandCollapseAsync(nodeId, action, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
