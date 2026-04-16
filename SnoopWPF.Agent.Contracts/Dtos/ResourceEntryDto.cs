namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single resource dictionary entry.
/// </summary>
[DataContract]
public sealed class ResourceDto
{
    [DataMember(Name = "key")]
    public string Key { get; set; } = string.Empty;

    [DataMember(Name = "valueTypeName")]
    public string ValueTypeName { get; set; } = string.Empty;

    [DataMember(Name = "valueSummary")]
    public string ValueSummary { get; set; } = string.Empty;

    [DataMember(Name = "origin")]
    public string Origin { get; set; } = string.Empty;

    [DataMember(Name = "dictionarySource")]
    public string DictionarySource { get; set; } = string.Empty;
}
