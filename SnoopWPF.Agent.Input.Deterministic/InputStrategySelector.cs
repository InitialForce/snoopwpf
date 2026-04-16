namespace SnoopWPF.Agent.Input.Deterministic;

using System;
using System.Collections.Generic;
using System.Windows;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Selects the best-matching <see cref="IDeterministicInputStrategy"/> for a given
/// <see cref="InputIntent"/> and target element, applying all session-policy gates.
/// </summary>
/// <remarks>
/// All tier/gate enforcement lives here per global rule S2. No tool handler may bypass
/// this selector (enforced by Roslyn analyzer in M1-16/17).
///
/// Gate priority (highest to lowest):
/// <list type="number">
///   <item>Injection mode → refuses all strategies (S7).</item>
///   <item>Automation gate → rejects Click/Toggle/ExpandCollapse when
///         <see cref="SessionPolicy.EnableAutomation"/> is <see langword="false"/>.</item>
///   <item>Mutation gate → rejects SetProperty when
///         <see cref="SessionPolicy.EnableMutation"/> is <see langword="false"/>.</item>
///   <item>MaxTier → rejects any strategy whose <see cref="IDeterministicInputStrategy.Tier"/>
///         exceeds <see cref="SessionPolicy.MaxTier"/>.</item>
///   <item>CanHandle → asks each remaining candidate whether it handles the intent/target.</item>
/// </list>
/// </remarks>
public sealed class InputStrategySelector
{
    private readonly SessionPolicy policy;
    private readonly IReadOnlyList<IDeterministicInputStrategy> strategies;

    /// <summary>
    /// Initialises the selector with an immutable <paramref name="sessionPolicy"/> and the full
    /// set of registered <paramref name="registeredStrategies"/>.
    /// </summary>
    /// <param name="sessionPolicy">Session policy. Treated as immutable (S1).</param>
    /// <param name="registeredStrategies">All registered strategy implementations.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sessionPolicy"/> or <paramref name="registeredStrategies"/> is null.
    /// </exception>
    public InputStrategySelector(SessionPolicy sessionPolicy, IReadOnlyList<IDeterministicInputStrategy> registeredStrategies)
    {
        this.policy = sessionPolicy ?? throw new ArgumentNullException(nameof(sessionPolicy));
        this.strategies = registeredStrategies ?? throw new ArgumentNullException(nameof(registeredStrategies));
    }

    /// <summary>
    /// Returns the best-matching strategy for the given <paramref name="intent"/> and
    /// <paramref name="target"/>, or <see langword="null"/> plus an appropriate
    /// <paramref name="failureReason"/> when the request is gated.
    /// </summary>
    /// <param name="intent">The requested action.</param>
    /// <param name="target">The WPF element to operate on.</param>
    /// <param name="failureReason">
    /// Set to the gate that blocked selection when <see langword="null"/> is returned;
    /// <see langword="null"/> when a strategy is returned.
    /// </param>
    /// <returns>
    /// A strategy that can handle the request, or <see langword="null"/> if gated/unavailable.
    /// </returns>
    public IDeterministicInputStrategy? Select(
        InputIntent intent,
        DependencyObject target,
        out FailureReason? failureReason)
    {
        if (intent is null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        // S7: Injection mode is inspection-only in MVP. Refuse all strategies.
        if (this.policy.Mode == SessionMode.Injection)
        {
            failureReason = FailureReason.TierMismatch;
            return null;
        }

        // Automation gate: Click, Toggle, ExpandCollapse require EnableAutomation.
        if (!this.policy.EnableAutomation && IsAutomationIntent(intent.Kind))
        {
            failureReason = FailureReason.AutomationDisabled;
            return null;
        }

        // Mutation gate: SetProperty requires EnableMutation.
        if (!this.policy.EnableMutation && intent.Kind == InputIntentKind.SetProperty)
        {
            failureReason = FailureReason.MutationDisabled;
            return null;
        }

        // Walk strategies in registration order and return the first that passes all gates.
        foreach (var strategy in this.strategies)
        {
            // MaxTier gate.
            if (strategy.Tier > this.policy.MaxTier)
            {
                continue;
            }

            // CanHandle gate.
            if (!strategy.CanHandle(intent, target))
            {
                continue;
            }

            failureReason = null;
            return strategy;
        }

        // No strategy passed all gates.
        failureReason = FailureReason.TierMismatch;
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> for intents that require UI Automation.
    /// </summary>
    private static bool IsAutomationIntent(InputIntentKind kind)
    {
        return kind is InputIntentKind.Click
                    or InputIntentKind.Toggle
                    or InputIntentKind.ExpandCollapse;
    }
}
