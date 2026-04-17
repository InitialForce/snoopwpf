namespace SnoopWPF.Agent.Engine.StateDelta;

using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Default <see cref="ISuggestionTranslator"/> implementation that delegates to
/// <see cref="FailureReasonDescriptor.Suggest"/> without any translation.
/// </summary>
/// <remarks>
/// Registered as a singleton via
/// <c>McpServerSetup.BuildServiceCollection</c>.  Consumer-side hosts that need
/// to rewrite advisory tool names (e.g. map <c>broker_launch_target</c> to a
/// product-specific tool name) may replace this registration with a custom
/// implementation or a decorator.
/// </remarks>
internal sealed class DefaultSuggestionTranslator : ISuggestionTranslator
{
    /// <inheritdoc />
    public SuggestionDto? Translate(FailureReason reason, FailureContext ctx)
    {
        return FailureReasonDescriptor.Suggest(reason, ctx.Locator);
    }
}
