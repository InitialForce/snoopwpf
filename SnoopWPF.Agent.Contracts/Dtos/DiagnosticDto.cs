namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single diagnostic item from a diagnostic provider.
/// </summary>
[DataContract]
public sealed class DiagnosticItemDto
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    [DataMember(Name = "area")]
    public string Area { get; set; } = string.Empty;

    [DataMember(Name = "level")]
    public string Level { get; set; } = string.Empty;

    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Name = "nodePath")]
    public List<string> NodePath { get; set; } = new List<string>();
}
