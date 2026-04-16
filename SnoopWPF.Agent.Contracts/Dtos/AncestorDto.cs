namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single entry in an element's ancestor chain.
/// </summary>
[DataContract]
public sealed class AncestorDto
{
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;
}
