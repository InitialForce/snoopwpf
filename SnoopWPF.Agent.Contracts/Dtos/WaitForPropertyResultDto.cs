namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of <c>wpf_wait_for_property</c> (M2-09).
/// </summary>
[DataContract]
public sealed class WaitForPropertyResultDto
{
    /// <summary>
    /// <see langword="true"/> when the condition was satisfied before the timeout.
    /// </summary>
    [DataMember(Name = "conditionMet")]
    public bool ConditionMet { get; init; }

    /// <summary>
    /// The last observed property value at the time the poll concluded (or <see langword="null"/>
    /// when <c>presenceExpected=absent</c> and the element was not found).
    /// </summary>
    [DataMember(Name = "actualValue")]
    public string? ActualValue { get; init; }

    /// <summary>
    /// Total elapsed wall-clock time in milliseconds from entry to return.
    /// </summary>
    [DataMember(Name = "elapsedMs")]
    public int ElapsedMs { get; init; }

    /// <summary>
    /// Number of poll iterations executed before the condition was satisfied or timed out.
    /// </summary>
    [DataMember(Name = "pollCount")]
    public int PollCount { get; init; }
}
