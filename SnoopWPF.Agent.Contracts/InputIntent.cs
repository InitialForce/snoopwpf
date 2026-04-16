namespace SnoopWPF.Agent.Contracts;

using System.Collections.Generic;
using System.Runtime.Serialization;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Represents a single act-tool intent passed to an <see cref="IDeterministicInputStrategy"/>.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> identifies which act-tool is being invoked.
/// <see cref="Arguments"/> carries tool-specific parameters (e.g. text value, item index).
/// The selector and every strategy must treat <see cref="InputIntent"/> as immutable.
/// </remarks>
[DataContract]
public sealed record InputIntent
{
    /// <summary>The kind of input action to perform.</summary>
    [DataMember(Name = "kind")]
    public InputIntentKind Kind { get; init; }

    /// <summary>
    /// Tool-specific arguments (name/value pairs).
    /// Never use <c>Dictionary&lt;string,string&gt;</c> — use this list for DataContract compat (rule 6).
    /// </summary>
    [DataMember(Name = "arguments")]
    public List<NameValuePairDto> Arguments { get; init; } = new();
}

/// <summary>
/// Enumeration of all act-tool intents handled by the deterministic input layer.
/// Each value maps 1:1 to a concrete MCP tool in M2.
/// </summary>
public enum InputIntentKind
{
    /// <summary>wpf_click — invoke the primary click action on a control.</summary>
    Click = 0,

    /// <summary>wpf_toggle — toggle a toggle-button or similar control.</summary>
    Toggle = 1,

    /// <summary>wpf_expand_collapse — expand or collapse a tree/menu node.</summary>
    ExpandCollapse = 2,

    /// <summary>wpf_set_check_state — set the IsChecked state of a checkbox.</summary>
    SetCheckState = 3,

    /// <summary>wpf_set_text_value — set text content of a TextBox or similar.</summary>
    SetTextValue = 4,

    /// <summary>wpf_select_item — select an item in a selector control.</summary>
    SelectItem = 5,

    /// <summary>wpf_execute_command — execute a routed/relay command (L0).</summary>
    ExecuteCommand = 6,

    /// <summary>wpf_set_property — directly set a dependency property value (mutation).</summary>
    SetProperty = 7,
}
