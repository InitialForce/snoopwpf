namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single entry in an element's ancestor chain.
/// </summary>
[DataContract]
public sealed class AncestorDto
{
    /// <summary>Node ID of this ancestor element.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Fully-qualified CLR type name of this ancestor.</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Value of the ancestor's <c>Name</c> / <c>x:Name</c> attribute, or empty when unnamed.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CLR type name of the ancestor's DataContext, or empty when null.</summary>
    [DataMember(Name = "dataContextType")]
    public string DataContextType { get; set; } = string.Empty;
}
