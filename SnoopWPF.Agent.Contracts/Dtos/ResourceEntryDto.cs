namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;

/// <summary>
/// A single resource dictionary entry.
/// </summary>
[DataContract]
public sealed class ResourceDto
{
    /// <summary>Resource dictionary key (string or type name for implicit styles).</summary>
    [DataMember(Name = "key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Short CLR type name of the resource value (e.g. <c>Style</c>, <c>SolidColorBrush</c>).</summary>
    [DataMember(Name = "valueTypeName")]
    public string ValueTypeName { get; set; } = string.Empty;

    /// <summary>Brief string representation of the resource value.</summary>
    [DataMember(Name = "valueSummary")]
    public string ValueSummary { get; set; } = string.Empty;

    /// <summary>Where the resource was found: <c>Local</c>, <c>Application</c>, or <c>System</c>.</summary>
    [DataMember(Name = "origin")]
    public string Origin { get; set; } = string.Empty;

    /// <summary>Display name of the <c>ResourceDictionary</c> that contains this entry (e.g. source URI).</summary>
    [DataMember(Name = "dictionarySource")]
    public string DictionarySource { get; set; } = string.Empty;
}
