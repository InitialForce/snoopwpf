namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// An attached behavior on a WPF element.
/// </summary>
[DataContract]
public sealed class BehaviorDto
{
    /// <summary>Fully-qualified CLR type name of the behavior.</summary>
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Short name of the assembly that defines the behavior type.</summary>
    [DataMember(Name = "assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Snapshot of the behavior's public properties at inspection time.</summary>
    [DataMember(Name = "properties")]
    public List<NameValuePairDto> Properties { get; set; } = new List<NameValuePairDto>();
}
