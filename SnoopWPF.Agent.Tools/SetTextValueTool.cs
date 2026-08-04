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
        "Set the text of a TextBox, PasswordBox, or RichTextBox via SetCurrentValue on the text DP " +
        "(L0, preserves TwoWay bindings); returns StateDeltaDto. PasswordBox input is sensitive — " +
        "previousValue is always \"[REDACTED]\"; RichTextBox is replaced with a single plain-text paragraph " +
        "(formatting discarded). Requires EnableMutation=true. See docs/mcp-tools-reference.md.")]
    public Task<string> SetTextValueAsync(
        [Description("Node ID of the element whose text should be set.")] string nodeId,
        [Description("The new text value to assign.")] string value,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.SetTextValueAsync(nodeId, value, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
