namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A name/value pair. Used instead of Dictionary&lt;string,string&gt; for DCJS/STJ serialization compatibility.
/// </summary>
[DataContract]
public sealed class NameValuePairDto
{
    /// <summary>The key or argument name.</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The associated string value.</summary>
    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;
}
