namespace SnoopWPF.Agent.Tools;

using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_set_text_value — set the text content of a TextBox, PasswordBox, or RichTextBox.
/// </summary>
[McpServerToolType]
public sealed class SetTextValueTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_set_text_value")]
    [Description(
        "Set the text content of a TextBox, PasswordBox, or RichTextBox via SetValue on the text " +
        "dependency property (L0). No raw Win32 input is used. " +
        "Returns StateDeltaDto with success, stateChanged, treeVersionDelta, and " +
        "failureReason/suggestion if the value could not be set. " +
        "\n\nGuidelines: " +
        "Use this tool instead of simulated keystrokes whenever the target is a text-input control. " +
        "For PasswordBox, the value is treated as SensitiveText (S3) and is redacted from all log " +
        "output; previousValue in the response is always '[REDACTED]'. " +
        "Mutation must be enabled (EnableMutation=true in SnoopAgentOptions). " +
        "For RichTextBox, this tool replaces the entire flow document with a single paragraph " +
        "containing the supplied plain text; existing formatting is discarded. " +
        "\n\nLimitations: " +
        "Does not support multi-paragraph rich text or inline formatting. " +
        "For PasswordBox, the PasswordProperty DP is used; Password.SecurePassword is not " +
        "accessible from the agent layer. " +
        "DataBinding: if the Text property has a two-way binding, the bound source will be " +
        "updated via the normal DP change notification path. " +
        "\n\nApplies to: " +
        "TextBox, PasswordBox, RichTextBox.")]
    public async Task<string> SetTextValueAsync(
        [Description("Node ID of the element whose text should be set.")] string nodeId,
        [Description("The new text value to assign.")] string value,
        CancellationToken ct = default)
    {
        try
        {
            var result = await inspector.SetTextValueAsync(nodeId, value, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
    }
}
