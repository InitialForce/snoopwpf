namespace SnoopWPF.Agent.Contracts;

using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Translates a <see cref="FailureReason"/> value plus optional call context into a
/// machine-executable <see cref="SuggestionDto"/> that the MCP client can act on
/// without human intervention.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are registered as singletons in the DI container.
/// The default implementation (<c>DefaultSuggestionTranslator</c>) delegates to
/// the static <c>FailureReasonDescriptor.Suggest</c> switch table.
/// Consumer-side implementations (e.g. a broker that rewrites
/// <c>broker_launch_target</c> → a product-specific tool name) can replace or
/// decorate the default registration.
/// </para>
/// <para>
/// The method returns <see langword="null"/> for failure reasons that require
/// operator-level configuration changes and therefore have no automated remediation
/// (e.g. <see cref="FailureReason.AutomationDisabled"/>,
/// <see cref="FailureReason.MutationDisabled"/>).
/// </para>
/// <para>
/// See <c>ARCHITECTURE-CHANGE-2026-04-16-SUGGESTION-TRANSLATOR.md</c> for the
/// pluggable translator design.
/// </para>
/// </remarks>
public interface ISuggestionTranslator
{
    /// <summary>
    /// Returns the canonical <see cref="SuggestionDto"/> for <paramref name="reason"/>
    /// in the given <paramref name="ctx"/>, or <see langword="null"/> when no automated
    /// remediation exists.
    /// </summary>
    /// <param name="reason">The failure reason to map.</param>
    /// <param name="ctx">
    /// Context about the failing call (element locator, extra hints).
    /// Use <see cref="FailureContext.Empty"/> when no context is available.
    /// </param>
    SuggestionDto? Translate(FailureReason reason, FailureContext ctx);
}
