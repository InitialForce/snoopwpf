namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Coarse-grained classification of a <see cref="Dtos.SuggestionDto"/>, used by
/// <c>ISuggestionTranslator</c> implementations to decide whether and how to rewrite
/// the tool name before forwarding the suggestion to the MCP client.
/// </summary>
/// <remarks>
/// <para>
/// Defined in the Contracts package (not BrokerHost) so that consumers that build
/// their own broker — or that host in co-located mode — can also consume the
/// extension point without taking a dependency on the BrokerHost package.
/// </para>
/// <para>
/// Serializes as its string name (not the integer value) to enable forward-
/// compatibility: adding new members is non-breaking for any consumer that
/// treats unknown category strings as <see cref="Other"/>.
/// </para>
/// <para>
/// See <c>ARCHITECTURE-CHANGE-2026-04-16-SUGGESTION-TRANSLATOR.md</c> for the
/// pluggable translator design that consumes this enum.
/// </para>
/// </remarks>
public enum SuggestionCategory
{
    /// <summary>
    /// The suggestion references a broker-lifecycle operation (launch, restart, attach).
    /// Consumers typically map these to product-specific tool names
    /// (e.g. <c>broker_launch_target</c> → <c>mc_launch</c>).
    /// The tool name carried in <see cref="Dtos.SuggestionDto.Tool"/> for this category
    /// is an advisory generic name; it is NOT a registered upstream MCP tool.
    /// </summary>
    BrokerLifecycle = 0,

    /// <summary>
    /// The suggestion recommends inspecting state (screenshot, property read,
    /// <c>wpf_get_tree</c>) because the last outcome was uncertain.
    /// </summary>
    StateInspection = 1,

    /// <summary>
    /// The suggestion recommends retrying the call after the cause has been fixed
    /// (e.g. <c>LOCATOR_NOT_FOUND</c> + retry same tool).
    /// </summary>
    Retry = 2,

    /// <summary>
    /// The suggestion relates to redaction / sensitive content (e.g. re-issue with
    /// <c>retainSensitive: false</c>).
    /// </summary>
    Redaction = 3,

    /// <summary>
    /// Suggestions that do not fit the above categories. Translators should pass
    /// these through unchanged.
    /// </summary>
    Other = 99,
}
