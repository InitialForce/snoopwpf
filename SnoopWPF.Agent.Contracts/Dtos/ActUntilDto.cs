namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A property-equality predicate consumed by <c>wpf_act_until</c>.
/// The polled property is read off <see cref="TargetNodeId"/> after each retry until
/// it matches <see cref="ExpectedValue"/> (when <see cref="PresenceExpected"/> is
/// <c>"present"</c>) or until the node disappears (<c>"absent"</c>).
/// </summary>
[DataContract]
public sealed class ActUntilPredicateDto
{
    /// <summary>Node identifier on which to poll <see cref="PropertyName"/>.</summary>
    [DataMember(Name = "targetNodeId")]
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>Dependency property name to poll. Case-insensitive match against DPs and CLR properties.</summary>
    [DataMember(Name = "propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Value the property is expected to equal (Ordinal compare on <c>ToString()</c>).
    /// Ignored when <see cref="PresenceExpected"/> is <c>"absent"</c>.
    /// </summary>
    [DataMember(Name = "expectedValue")]
    public string? ExpectedValue { get; set; }

    /// <summary>
    /// One of <c>present</c> (default) or <c>absent</c>. With <c>present</c> the predicate
    /// is satisfied when the node resolves and its property equals <see cref="ExpectedValue"/>.
    /// With <c>absent</c> it is satisfied when the node fails to resolve (e.g. the dialog closed).
    /// </summary>
    [DataMember(Name = "presenceExpected")]
    public string PresenceExpected { get; set; } = "present";
}

/// <summary>
/// Result of <c>wpf_act_until</c>: the action's per-step outcome plus the poll outcome.
/// </summary>
[DataContract]
public sealed class ActUntilResultDto
{
    /// <summary>Outcome of the action that fired before polling.</summary>
    [DataMember(Name = "actionResult")]
    public ActionStepResultDto ActionResult { get; set; } = new ActionStepResultDto();

    /// <summary>
    /// <see langword="true"/> when the action succeeded AND the predicate matched within
    /// <c>timeoutMs</c>. <see langword="false"/> on action failure or poll timeout.
    /// </summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary><see langword="true"/> when the predicate was satisfied during polling.</summary>
    [DataMember(Name = "predicateMet")]
    public bool PredicateMet { get; set; }

    /// <summary><see langword="true"/> when the poll loop exited because the deadline passed.</summary>
    [DataMember(Name = "timedOut")]
    public bool TimedOut { get; set; }

    /// <summary>
    /// Last observed value of the polled property (or null when the node was absent or the
    /// property couldn't be read). Useful for diagnosing why the predicate did not match.
    /// </summary>
    [DataMember(Name = "actualValue")]
    public string? ActualValue { get; set; }

    /// <summary>Wall-clock milliseconds elapsed during polling (excludes the action itself).</summary>
    [DataMember(Name = "elapsedMs")]
    public int ElapsedMs { get; set; }

    /// <summary>Number of poll iterations executed.</summary>
    [DataMember(Name = "pollCount")]
    public int PollCount { get; set; }
}
