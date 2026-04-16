namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Basic information about a target process.
/// </summary>
[DataContract]
public sealed class ProcessInfoDto
{
    [DataMember(Name = "pid")]
    public int Pid { get; set; }

    [DataMember(Name = "processName")]
    public string ProcessName { get; set; } = string.Empty;

    [DataMember(Name = "mainWindowTitle")]
    public string MainWindowTitle { get; set; } = string.Empty;
}
