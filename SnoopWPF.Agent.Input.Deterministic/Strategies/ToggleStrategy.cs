namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L1 strategy: flips the toggle state of a WPF element via
/// <see cref="UIElementAutomationPeer"/> / <see cref="IToggleProvider"/>.
/// </summary>
/// <remarks>
/// This strategy uses the UI Automation <see cref="IToggleProvider"/> pattern,
/// which maps to the element's native toggle behaviour without raw Win32 input.
/// The result is non-deterministic — it always flips to the opposite state.
/// Use <c>wpf_set_check_state</c> (L0) when a specific final state is required.
///
/// Rejection: <see cref="CheckBox"/> and <see cref="RadioButton"/> are rejected with
/// <see cref="FailureReason.PatternNotSupported"/> and a <c>wpf_set_check_state</c>
/// suggestion per PRD §5.2 — callers should use the deterministic L0 tool instead.
///
/// Gate: <see cref="InputIntentKind.Toggle"/> requires automation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class ToggleStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L1;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when:
    /// <list type="bullet">
    ///   <item>The intent kind is <see cref="InputIntentKind.Toggle"/>.</item>
    ///   <item>The target is a <see cref="UIElement"/>.</item>
    ///   <item>An <see cref="AutomationPeer"/> can be created for the element.</item>
    ///   <item>The peer supports <see cref="PatternInterface.Toggle"/>.</item>
    /// </list>
    /// <see cref="CheckBox"/> and <see cref="RadioButton"/> are claimed here so that
    /// <see cref="Invoke"/> can return the canonical <c>wpf_set_check_state</c> rejection
    /// rather than falling through to an unsupported-strategy error.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.Toggle)
        {
            return false;
        }

        if (target is not UIElement uiElement)
        {
            return false;
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        return peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider;
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

        // CheckBox and RadioButton: reject with wpf_set_check_state suggestion (PRD §5.2).
        if (uiElement is CheckBox or RadioButton)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        var toggleProvider = peer?.GetPattern(PatternInterface.Toggle) as IToggleProvider;

        if (toggleProvider is null)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        // Toggle via IToggleProvider — flips current state (non-deterministic).
        toggleProvider.Toggle();

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] ToggleStrategy.Invoke: type={uiElement.GetType().Name}");

        return new DeterministicInputResult
        {
            Success = true,
            ChosenTier = InputTier.L1,
        };
    }
}
