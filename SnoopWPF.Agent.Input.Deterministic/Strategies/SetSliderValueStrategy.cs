namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L0 strategy: sets a <see cref="RangeBase"/> (Slider, ScrollBar, ProgressBar) value using
/// either an absolute value or a normalised 0..1 fraction that maps to the control's
/// <see cref="RangeBase.Minimum"/>…<see cref="RangeBase.Maximum"/> range.
/// </summary>
/// <remarks>
/// Added in M2-16 (bd-2co) to close the §12.3 "Slider value setter + range normalization"
/// coverage gap identified in the M0-06 audit.  The normalised form (<c>normalized=true</c>)
/// is the preferred shim API per MC §8 item 1 — it decouples test steps from the raw slider
/// range so that changing the Minimum/Maximum in the app does not break scenario steps.
///
/// Argument contract:
/// <list type="bullet">
///   <item><c>"value"</c> — a double string in InvariantCulture (required).</item>
///   <item><c>"normalized"</c> — <c>"true"</c> or <c>"false"</c> (default <c>"false"</c>).
///         When <c>"true"</c> the <c>"value"</c> is interpreted as a fraction in [0, 1] and
///         mapped to <c>Minimum + fraction × (Maximum − Minimum)</c>.</item>
/// </list>
///
/// Gate: <see cref="InputIntentKind.SetSliderValue"/> requires mutation to be enabled
/// (enforced by <see cref="InputStrategySelector"/> before this strategy is invoked).
/// </remarks>
public sealed class SetSliderValueStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L0;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when the target is a <see cref="RangeBase"/>
    /// (which includes <see cref="Slider"/>, <see cref="ScrollBar"/>, and
    /// <see cref="ProgressBar"/>) and the intent kind is
    /// <see cref="InputIntentKind.SetSliderValue"/>.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.SetSliderValue)
        {
            return false;
        }

        return target is RangeBase;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        if (target is not RangeBase rangeBase)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L0,
            };
        }

        var valueStr = GetArgument(intent, "value");
        if (string.IsNullOrEmpty(valueStr) ||
            !double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawValue))
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.ElementNotFound, // closest available reason for bad argument
                ChosenTier = InputTier.L0,
            };
        }

        var normalizedStr = GetArgument(intent, "normalized") ?? "false";
        var isNormalized = string.Equals(normalizedStr, "true", StringComparison.OrdinalIgnoreCase);

        double targetValue;
        if (isNormalized)
        {
            // Clamp fraction to [0, 1] to guard against caller rounding errors.
            var fraction = Math.Max(0.0, Math.Min(1.0, rawValue));
            var min = rangeBase.Minimum;
            var max = rangeBase.Maximum;
            var range = max - min;

            // Degenerate range: both ends equal → set to minimum.
            targetValue = range <= 0.0 ? min : min + (fraction * range);
        }
        else
        {
            // Absolute value: clamp to [Minimum, Maximum].
            targetValue = Math.Max(rangeBase.Minimum, Math.Min(rangeBase.Maximum, rawValue));
        }

        var previousValue = rangeBase.Value;

        // Use SetValue through the DP path so change notifications and bindings fire correctly.
        rangeBase.SetValue(RangeBase.ValueProperty, targetValue);

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SetSliderValue({rangeBase.GetType().Name}): " +
            $"normalized={isNormalized}, rawInput={rawValue}, " +
            $"targetValue={targetValue}, previousValue={previousValue}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = previousValue.ToString(CultureInfo.InvariantCulture),
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? GetArgument(InputIntent intent, string name)
    {
        if (intent.Arguments is null)
        {
            return null;
        }

        foreach (var pair in intent.Arguments)
        {
            if (string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
