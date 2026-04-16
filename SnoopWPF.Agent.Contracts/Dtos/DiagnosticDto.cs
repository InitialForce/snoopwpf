namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A single diagnostic item from a diagnostic provider.
/// </summary>
[DataContract]
public sealed class DiagnosticItemDto
{
    /// <summary>Short diagnostic rule name (e.g. <c>BindingError</c>, <c>NullDataContext</c>).</summary>
    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the diagnostic finding.</summary>
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Diagnostic category grouping (e.g. <c>Binding</c>, <c>Performance</c>, <c>Accessibility</c>).</summary>
    [DataMember(Name = "area")]
    public string Area { get; set; } = string.Empty;

    /// <summary>Severity level: <c>Info</c>, <c>Warning</c>, or <c>Error</c>.</summary>
    [DataMember(Name = "level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>Node ID of the element the diagnostic is associated with.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Ordered list of node IDs from the root to the affected element.</summary>
    [DataMember(Name = "nodePath")]
    public List<string> NodePath { get; set; } = new List<string>();
}
