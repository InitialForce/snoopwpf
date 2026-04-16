namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Result of <c>wpf_pump_until_idle</c> (M2-11).
/// </summary>
[DataContract]
public sealed class PumpUntilIdleResultDto
{
    /// <summary>
    /// <see langword="true"/> when all monitored resources reached idle before the timeout.
    /// </summary>
    [DataMember(Name = "idleReached")]
    public bool IdleReached { get; init; }

    /// <summary>
    /// Total elapsed wall-clock time in milliseconds from entry to return.
    /// </summary>
    [DataMember(Name = "elapsedMs")]
    public int ElapsedMs { get; init; }

    /// <summary>
    /// Names of resources that were monitored during this call.
    /// </summary>
    [DataMember(Name = "resourcesMonitored")]
    public List<string> ResourcesMonitored { get; init; } = new();

    /// <summary>
    /// Names of resources that were still busy when the method returned
    /// (empty when <see cref="IdleReached"/> is <see langword="true"/>).
    /// </summary>
    [DataMember(Name = "stillBusy")]
    public List<string> StillBusy { get; init; } = new();
}
