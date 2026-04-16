namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A condition in a trigger (e.g. Property=X is Value=Y).
/// </summary>
[DataContract]
public sealed class TriggerConditionDto
{
    /// <summary>Name of the dependency property evaluated by this condition.</summary>
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

    /// <summary>Value the property must equal for the condition to be satisfied.</summary>
    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A setter in a trigger (sets Property to Value when trigger fires).
/// </summary>
[DataContract]
public sealed class TriggerSetterDto
{
    /// <summary>Name of the dependency property this setter targets.</summary>
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

    /// <summary>Value applied to the property when the trigger fires.</summary>
    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A trigger defined on a WPF element (Style, ControlTemplate, DataTemplate, or Element).
/// </summary>
[DataContract]
public sealed class TriggerDto
{
    /// <summary>
    /// The trigger type name (e.g. "Trigger", "DataTrigger", "EventTrigger", "MultiTrigger").
    /// </summary>
    [DataMember(Name = "triggerType")]
    public string TriggerType { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when all trigger conditions are currently satisfied.</summary>
    [DataMember(Name = "isActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Source of the trigger: "Style", "ControlTemplate", "DataTemplate", or "Element".
    /// Matches <c>TriggerSource</c> enum values in Snoop.Core.
    /// </summary>
    [DataMember(Name = "source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Conditions that must all be satisfied for this trigger to fire.</summary>
    [DataMember(Name = "conditions")]
    public List<TriggerConditionDto> Conditions { get; set; } = new List<TriggerConditionDto>();

    /// <summary>Property setters applied while the trigger is active.</summary>
    [DataMember(Name = "setters")]
    public List<TriggerSetterDto> Setters { get; set; } = new List<TriggerSetterDto>();
}
