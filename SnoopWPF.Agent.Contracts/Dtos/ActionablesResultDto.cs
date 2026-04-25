namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single actionable element returned by <c>wpf_get_actionables</c>.
/// Compact payload optimized for LLM consumption — only the fields needed
/// to decide which control to interact with next.
/// </summary>
[DataContract]
public sealed class ActionableDto
{
    /// <summary>Opaque node identifier. Pass to <c>wpf_click</c>, <c>wpf_set_text_value</c>, etc.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Coarse interaction kind. One of: <c>button</c>, <c>input</c>, <c>checkbox</c>,
    /// <c>radio</c>, <c>toggle</c>, <c>menuitem</c>, <c>hyperlink</c>, <c>combobox</c>,
    /// <c>listitem</c>, <c>slider</c>, <c>expander</c>, <c>tab</c>.
    /// </summary>
    [DataMember(Name = "kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Best-effort human-readable label. Resolution order:
    /// <c>AutomationProperties.Name</c> → <c>AutomationProperties.HelpText</c> →
    /// <c>ContentControl.Content</c> (string) → <c>TextBlock.Text</c> →
    /// <c>HeaderedItemsControl.Header</c> (string) → empty.
    /// </summary>
    [DataMember(Name = "label")]
    public string Label { get; set; } = string.Empty;

    /// <summary><c>x:Name</c> attribute, or empty when unnamed.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary><c>AutomationProperties.AutomationId</c>, or empty when unset.</summary>
    [DataMember(Name = "automationId")]
    public string AutomationId { get; set; } = string.Empty;

    /// <summary>CLR type name without namespace (e.g. <c>Button</c>, <c>TextBox</c>).</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when <c>IsEnabled</c> AND visible (rendered + ActualWidth/Height &gt; 0).</summary>
    [DataMember(Name = "isEnabled")]
    public bool IsEnabled { get; set; }

    /// <summary>
    /// <see langword="true"/> when the control has a non-null <c>ButtonBase.Command</c> binding.
    /// L0 hint — prefer <c>wpf_execute_command</c> over <c>wpf_click</c> on these.
    /// </summary>
    [DataMember(Name = "hasCommandBinding")]
    public bool HasCommandBinding { get; set; }
}

/// <summary>
/// Result of <c>wpf_get_actionables</c>.
/// </summary>
[DataContract]
public sealed class ActionablesResultDto
{
    /// <summary>Visible, enabled actionable elements found in the requested subtree.</summary>
    [DataMember(Name = "items")]
    public List<ActionableDto> Items { get; set; } = new List<ActionableDto>();

    /// <summary>Total number of nodes examined during the walk.</summary>
    [DataMember(Name = "totalScanned")]
    public int TotalScanned { get; set; }

    /// <summary><see langword="true"/> when the result list was cut at <c>maxResults</c>.</summary>
    [DataMember(Name = "truncated")]
    public bool Truncated { get; set; }
}
