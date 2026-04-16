namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L1 strategy: invokes the primary click action on a WPF element via
/// <see cref="UIElementAutomationPeer"/> / <see cref="IInvokeProvider"/>.
/// </summary>
/// <remarks>
/// This strategy uses the UI Automation <see cref="IInvokeProvider"/> pattern,
/// which maps to the element's native click behaviour without raw Win32 input.
/// Gate: <see cref="InputIntentKind.Click"/> requires automation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
///
/// Command hint: when the target element has a <see cref="ICommand"/> bound via
/// <see cref="ButtonBase.CommandProperty"/>, the result always carries a
/// <see cref="DeterministicInputResult"/> with <c>SuggestExecuteCommand = true</c>
/// — the act-tool layer surfaces this as a <c>wpf_execute_command</c> suggestion
/// so the calling agent can prefer the L0 path in future invocations.
/// </remarks>
public sealed class ClickStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L1;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when:
    /// <list type="bullet">
    ///   <item>The intent kind is <see cref="InputIntentKind.Click"/>.</item>
    ///   <item>The target is a <see cref="UIElement"/>.</item>
    ///   <item>An <see cref="AutomationPeer"/> can be created for the element.</item>
    ///   <item>The peer supports <see cref="PatternInterface.Invoke"/>.</item>
    /// </list>
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.Click)
        {
            return false;
        }

        if (target is not UIElement uiElement)
        {
            return false;
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        return peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        if (target is not UIElement uiElement)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        var invokeProvider = peer?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;

        if (invokeProvider is null)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        // Check whether the target has a Command bound — if so we will note that
        // wpf_execute_command would be preferred (L0 > L1 per PRD §4.4 tier ordering).
        var hasCommandBound = target.GetValue(ButtonBase.CommandProperty) is ICommand;

        // Invoke the automation pattern.
        invokeProvider.Invoke();

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] ClickStrategy.Invoke: hasCommandBound={hasCommandBound}");

        return new DeterministicInputResult
        {
            Success = true,
            ChosenTier = InputTier.L1,
            // When a Command is bound, set SuggestExecuteCommand so the tool layer
            // can attach the wpf_execute_command hint in the StateDeltaDto response.
            SuggestExecuteCommand = hasCommandBound,
        };
    }
}
