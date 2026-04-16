namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A condition in a trigger (e.g. Property=X is Value=Y).
/// </summary>
[DataContract]
public sealed class TriggerConditionDto
{
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A setter in a trigger (sets Property to Value when trigger fires).
/// </summary>
[DataContract]
public sealed class TriggerSetterDto
{
    [DataMember(Name = "property")]
    public string Property { get; set; } = string.Empty;

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

    [DataMember(Name = "isActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Source of the trigger: "Style", "ControlTemplate", "DataTemplate", or "Element".
    /// Matches <c>TriggerSource</c> enum values in Snoop.Core.
    /// </summary>
    [DataMember(Name = "source")]
    public string Source { get; set; } = string.Empty;

    [DataMember(Name = "conditions")]
    public List<TriggerConditionDto> Conditions { get; set; } = new List<TriggerConditionDto>();

    [DataMember(Name = "setters")]
    public List<TriggerSetterDto> Setters { get; set; } = new List<TriggerSetterDto>();
}
