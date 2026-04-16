namespace SnoopWPF.Agent.Contracts.Dtos;

using System;
using System.Runtime.Serialization;

/// <summary>
/// Metadata about a captured screenshot.
/// </summary>
[DataContract]
public sealed class ScreenshotMetadataDto
{
    [DataMember(Name = "width")]
    public int Width { get; set; }

    [DataMember(Name = "height")]
    public int Height { get; set; }

    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

/// <summary>
/// Result of a screenshot capture operation, containing metadata and raw PNG bytes.
/// </summary>
[DataContract]
public sealed class ScreenshotResultDto
{
    [DataMember(Name = "metadata")]
    public ScreenshotMetadataDto Metadata { get; set; } = new ScreenshotMetadataDto();

    [DataMember(Name = "pngBytes")]
    public byte[] PngBytes { get; set; } = Array.Empty<byte>();
}
