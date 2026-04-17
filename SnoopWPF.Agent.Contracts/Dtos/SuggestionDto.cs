namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// A machine-executable remediation suggestion returned inside <see cref="StateDeltaDto"/>
/// when an act-tool fails (PRD §7.5, FD-3).
/// </summary>
/// <remarks>
/// <para>
/// <c>Args</c> uses <see cref="NameValuePairDto"/> rather than
/// <c>Dictionary&lt;string,string&gt;</c> for DCJS/STJ serialization compatibility.
/// </para>
/// <para>
/// <b>Broker-lifecycle suggestions</b> (<see cref="Category"/> ==
/// <see cref="SuggestionCategory.BrokerLifecycle"/>): the <see cref="Tool"/> value
/// is an advisory generic name (e.g. <c>broker_launch_target</c>) that is NOT a
/// registered upstream MCP tool. Consumer-side <c>ISuggestionTranslator</c>
/// implementations translate it into a product-specific tool name before forwarding
/// the response frame to the MCP client. See
/// <c>ARCHITECTURE-CHANGE-2026-04-16-SUGGESTION-TRANSLATOR.md</c>.
/// </para>
/// </remarks>
[DataContract]
public sealed record SuggestionDto
{
    /// <summary>
    /// The MCP tool name to invoke (e.g. <c>wpf_inspect_element</c>).
    /// For <see cref="SuggestionCategory.BrokerLifecycle"/> suggestions this is an
    /// advisory generic name that consumer translators rewrite; it is NOT a registered
    /// upstream MCP tool.
    /// </summary>
    [DataMember(Name = "tool")]
    public string Tool { get; init; } = string.Empty;

    /// <summary>Arguments to pass to the tool, as ordered name/value pairs.</summary>
    [DataMember(Name = "args")]
    public List<NameValuePairDto> Args { get; init; } = new();

    /// <summary>
    /// Coarse-grained classification of this suggestion.
    /// Used by <c>ISuggestionTranslator</c> implementations to decide whether and how
    /// to rewrite <see cref="Tool"/> before forwarding the suggestion to the MCP client.
    /// Defaults to <see cref="SuggestionCategory.Other"/> so that existing callsites
    /// that do not set the field are backward-compatible.
    /// </summary>
    [DataMember(Name = "category")]
    public SuggestionCategory Category { get; init; } = SuggestionCategory.Other;
}
