namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// Result of a diagnostics export operation.
/// </summary>
[DataContract]
public sealed class ExportResultDto
{
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    [DataMember(Name = "filePath")]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
