namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of a diagnostics export operation.
/// </summary>
[DataContract]
public sealed class ExportResultDto
{
    /// <summary><see langword="true"/> when the export completed without error.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary>Absolute path of the exported file on the agent host machine.</summary>
    [DataMember(Name = "filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Error description when <see cref="Success"/> is <see langword="false"/>; null otherwise.</summary>
    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
