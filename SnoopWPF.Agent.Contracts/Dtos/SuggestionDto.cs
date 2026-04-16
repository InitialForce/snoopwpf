namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A machine-executable remediation suggestion returned inside <see cref="StateDeltaDto"/>
/// when an act-tool fails (PRD §7.5, FD-3).
/// </summary>
/// <remarks>
/// <c>Args</c> uses <see cref="NameValuePairDto"/> rather than
/// <c>Dictionary&lt;string,string&gt;</c> for DCJS/STJ serialization compatibility.
/// </remarks>
[DataContract]
public sealed record SuggestionDto
{
    /// <summary>The MCP tool name to invoke (e.g. <c>wpf_inspect_element</c>).</summary>
    [DataMember(Name = "tool")]
    public string Tool { get; init; } = string.Empty;

    /// <summary>Arguments to pass to the tool, as ordered name/value pairs.</summary>
    [DataMember(Name = "args")]
    public List<NameValuePairDto> Args { get; init; } = new();
}
