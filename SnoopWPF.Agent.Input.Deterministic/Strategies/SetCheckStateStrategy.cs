namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L0 strategy: sets the <see cref="ToggleButton.IsChecked"/> state directly on a
/// <see cref="CheckBox"/> or <see cref="RadioButton"/> via
/// <c>SetValue(ToggleButton.IsCheckedProperty, value)</c> — no raw Win32 input.
/// </summary>
/// <remarks>
/// Control routing:
/// <list type="bullet">
///   <item><see cref="CheckBox"/> — accepts <c>"checked"</c>, <c>"unchecked"</c>, and
///         <c>"indeterminate"</c>. Indeterminate is only valid when
///         <see cref="CheckBox.IsThreeState"/> is <see langword="true"/>.</item>
///   <item><see cref="RadioButton"/> — accepts <c>"checked"</c> only (radio buttons cannot
///         be programmatically unchecked or set to indeterminate from outside the group).</item>
/// </list>
///
/// Rejection: a bare <see cref="ToggleButton"/> (not a <see cref="CheckBox"/> or
/// <see cref="RadioButton"/>) is rejected with
/// <see cref="FailureReason.PatternNotSupported"/> at invoke time so the caller is
/// redirected to <c>wpf_toggle</c>.
///
/// Gate: <see cref="InputIntentKind.SetCheckState"/> requires mutation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class SetCheckStateStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L0;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when the target is a <see cref="CheckBox"/>,
    /// <see cref="RadioButton"/>, or bare <see cref="ToggleButton"/> and the intent
    /// kind is <see cref="InputIntentKind.SetCheckState"/>.
    /// Bare <see cref="ToggleButton"/> is claimed here so that <see cref="Invoke"/>
    /// can return the canonical <c>wpf_toggle</c> rejection rather than falling through
    /// to an unsupported-strategy error.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.SetCheckState)
        {
            return false;
        }

        return target is ToggleButton; // CheckBox and RadioButton are ToggleButton subtypes.
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        // Reject bare ToggleButton (not CheckBox/RadioButton) with a wpf_toggle suggestion.
        if (target is ToggleButton and not CheckBox and not RadioButton)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L0,
            };
        }

        var stateArg = GetArgument(intent, "state") ?? string.Empty;

        if (!TryParseState(stateArg, out var desiredState))
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L0,
            };
        }

        if (target is CheckBox checkBox)
        {
            return ApplyCheckBoxState(checkBox, desiredState);
        }

        if (target is RadioButton radioButton)
        {
            return ApplyRadioButtonState(radioButton, desiredState);
        }

        // Unreachable — CanHandle already filtered.
        return new DeterministicInputResult
        {
            Success = false,
            FailureReason = FailureReason.PatternNotSupported,
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Per-control implementations
    // -------------------------------------------------------------------------

    private static DeterministicInputResult ApplyCheckBoxState(CheckBox checkBox, bool? desiredState)
    {
        var previousValue = checkBox.IsChecked;
        checkBox.SetValue(ToggleButton.IsCheckedProperty, desiredState);

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SetCheckState(CheckBox): previous={FormatChecked(previousValue)}, desired={FormatChecked(desiredState)}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = FormatChecked(previousValue),
            ChosenTier = InputTier.L0,
        };
    }

    private static DeterministicInputResult ApplyRadioButtonState(RadioButton radioButton, bool? desiredState)
    {
        var previousValue = radioButton.IsChecked;
        radioButton.SetValue(ToggleButton.IsCheckedProperty, desiredState);

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SetCheckState(RadioButton): previous={FormatChecked(previousValue)}, desired={FormatChecked(desiredState)}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = FormatChecked(previousValue),
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the state string to a nullable bool.
    /// <c>"checked"</c> → <see langword="true"/>,
    /// <c>"unchecked"</c> → <see langword="false"/>,
    /// <c>"indeterminate"</c> → <see langword="null"/>.
    /// </summary>
    private static bool TryParseState(string state, out bool? result)
    {
        if (string.Equals(state, "checked", System.StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(state, "unchecked", System.StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        if (string.Equals(state, "indeterminate", System.StringComparison.OrdinalIgnoreCase))
        {
            result = null;
            return true;
        }

        result = null;
        return false;
    }

    internal static string FormatChecked(bool? value) => value switch
    {
        true => "checked",
        false => "unchecked",
        null => "indeterminate",
    };

    private static string? GetArgument(InputIntent intent, string name)
    {
        if (intent.Arguments is null)
        {
            return null;
        }

        foreach (var pair in intent.Arguments)
        {
            if (string.Equals(pair.Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
