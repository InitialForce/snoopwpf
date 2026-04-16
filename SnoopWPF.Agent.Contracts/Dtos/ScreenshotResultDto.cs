namespace SnoopWPF.Agent.Contracts.Dtos;

using System;
using System.Runtime.Serialization;

/// <summary>
/// Metadata about a captured screenshot.
/// </summary>
[DataContract]
public sealed class ScreenshotMetadataDto
{
    /// <summary>Width of the captured image in pixels.</summary>
    [DataMember(Name = "width")]
    public int Width { get; set; }

    /// <summary>Height of the captured image in pixels.</summary>
    [DataMember(Name = "height")]
    public int Height { get; set; }

    /// <summary>Node ID of the element that was captured.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;
}

/// <summary>
/// Result of a screenshot capture operation, containing metadata and raw PNG bytes.
/// </summary>
[DataContract]
public sealed class ScreenshotResultDto
{
    /// <summary>Metadata about the captured image (dimensions, source node).</summary>
    [DataMember(Name = "metadata")]
    public ScreenshotMetadataDto Metadata { get; set; } = new ScreenshotMetadataDto();

    /// <summary>Raw PNG-encoded image bytes.</summary>
    [DataMember(Name = "pngBytes")]
    public byte[] PngBytes { get; set; } = Array.Empty<byte>();
}
