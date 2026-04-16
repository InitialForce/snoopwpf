namespace SnoopWPF.Agent.Engine.StateDelta;

using System;
using System.Windows;

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
}
