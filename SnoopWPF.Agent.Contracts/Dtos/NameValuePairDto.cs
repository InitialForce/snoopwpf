namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A name/value pair. Used instead of Dictionary&lt;string,string&gt; for DCJS/STJ serialization compatibility.
/// </summary>
[DataContract]
public sealed class NameValuePairDto
{
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}
