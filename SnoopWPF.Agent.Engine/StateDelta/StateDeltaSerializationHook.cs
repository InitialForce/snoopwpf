namespace SnoopWPF.Agent.Engine.StateDelta;

using System;
using System.Windows;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Serialization-time helper that recomputes <c>stateChanged</c> by reading the
/// observable DP value <em>after</em> the set operation has fully settled (including
/// any re-entrant <see cref="PropertyChangedCallback"/> that may have reverted the
/// value).
///
/// PRD §7.3 W3-C1: <c>stateChanged</c> must reflect the actual post-call state, not
/// the value that was <em>requested</em>.  A reverting coerce/PropertyChangedCallback
/// can cause the two to diverge — this hook closes that false-positive.
/// </summary>
/// <remarks>
/// Called by <see cref="SnoopWPF.Agent.Engine.SnoopInspector"/> just before the
/// <see cref="SnoopWPF.Agent.Contracts.Dtos.StateDeltaDto"/> is returned to the
/// caller.  Must be invoked on the WPF Dispatcher thread.
/// </remarks>
public static class StateDeltaSerializationHook
{
    /// <summary>
    /// Reads the current effective value of <paramref name="property"/> on
    /// <paramref name="target"/> and compares it (as a string) to
    /// <paramref name="previousValue"/>.
    /// </summary>
    /// <param name="target">The <see cref="DependencyObject"/> that owns the property.</param>
    /// <param name="property">The <see cref="DependencyProperty"/> that was set.</param>
    /// <param name="previousValue">
    /// The string representation of the property value captured <em>before</em> the
    /// set operation (see M1-09 capture in <c>SnoopInspector.SetPropertyAsync</c>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the observable state differs from
    /// <paramref name="previousValue"/>; <see langword="false"/> when it is the same
    /// (e.g. because a reverting <see cref="PropertyChangedCallback"/> reset it).
    /// </returns>
    public static bool ComputeStateChanged(
        DependencyObject target,
        DependencyProperty property,
        string previousValue)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        // Read the actual settled value from the DP system — this reflects any
        // coerce or revert applied by a PropertyChangedCallback.
        var currentRaw = target.GetValue(property);
        var currentString = currentRaw?.ToString() ?? string.Empty;

        return !string.Equals(previousValue, currentString, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the canonical STATE_UNCHANGED <see cref="StateDeltaDto"/> per PRD §7.6:
    /// <c>{ success: true, stateChanged: false, treeVersionDelta: 0,
    /// failureReason: STATE_UNCHANGED, suggestion: wpf_wait_for_property }</c>.
    /// </summary>
    /// <param name="locator">
    /// The locator context used to populate the suggestion args.  Pass
    /// <see langword="null"/> when no element context is available.
    /// </param>
    /// <param name="hint">
    /// Optional extra hint text forwarded to <see cref="FailureReasonDescriptor.Suggest"/>.
    /// Currently unused by the descriptor but reserved for future overloads.
    /// </param>
    /// <returns>A fully-populated STATE_UNCHANGED <see cref="StateDeltaDto"/>.</returns>
    public static StateDeltaDto CreateUnchanged(WpfLocator? locator, string? hint = null)
    {
        return new StateDeltaDto
        {
            Success = true,
            StateChanged = false,
            TreeVersionDelta = 0,
            FailureReason = FailureReason.StateUnchanged,
            Suggestion = FailureReasonDescriptor.Suggest(FailureReason.StateUnchanged, locator),
        };
    }
}
