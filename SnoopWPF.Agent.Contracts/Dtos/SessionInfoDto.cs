namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Information about a single WPF Dispatcher in a session.
/// </summary>
[DataContract]
public sealed class DispatcherInfoDto
{
    [DataMember(Name = "id")]
    public int Id { get; set; }

    [DataMember(Name = "threadId")]
    public int ThreadId { get; set; }

    [DataMember(Name = "windowNodeIds")]
    public List<string> WindowNodeIds { get; set; } = new List<string>();
}

/// <summary>
/// Session-level information returned by wpf_get_session_info.
/// </summary>
[DataContract]
public sealed class SessionInfoDto
{
    [DataMember(Name = "processName")]
    public string ProcessName { get; set; } = string.Empty;

    [DataMember(Name = "pid")]
    public int Pid { get; set; }

    [DataMember(Name = "dotnetVersion")]
    public string DotnetVersion { get; set; } = string.Empty;

    [DataMember(Name = "mutationEnabled")]
    public bool MutationEnabled { get; set; }

    [DataMember(Name = "dispatchers")]
    public List<DispatcherInfoDto> Dispatchers { get; set; } = new List<DispatcherInfoDto>();

    [DataMember(Name = "capabilities")]
    public List<string> Capabilities { get; set; } = new List<string>();
}
