namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// An attached behavior on a WPF element.
/// </summary>
[DataContract]
public sealed class BehaviorDto
{
    [DataMember(Name = "typeName")]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [DataMember(Name = "properties")]
    public List<NameValuePairDto> Properties { get; set; } = new List<NameValuePairDto>();
}
