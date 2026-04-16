namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Basic information about a target process.
/// </summary>
[DataContract]
public sealed class ProcessInfoDto
{
    /// <summary>Operating-system process ID.</summary>
    [DataMember(Name = "pid")]
    public int Pid { get; set; }

    /// <summary>Name of the process executable (without extension).</summary>
    [DataMember(Name = "processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Title of the process main window, or empty when the process has no visible window.</summary>
    [DataMember(Name = "mainWindowTitle")]
    public string MainWindowTitle { get; set; } = string.Empty;
}
