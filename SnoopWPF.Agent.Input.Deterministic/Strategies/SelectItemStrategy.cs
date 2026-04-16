namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L0 strategy: selects an item in a non-virtualized <see cref="ItemsControl"/> by setting
/// <see cref="Selector.SelectedItem"/> or <see cref="Selector.SelectedIndex"/> directly via
/// <c>SetValue</c> — no raw Win32 input or UI Automation.
/// </summary>
/// <remarks>
/// Identifier forms (passed as the <c>"identifier"</c> argument in <see cref="InputIntent"/>):
/// <list type="bullet">
///   <item><b>Index</b> — a plain integer string (e.g. <c>"0"</c>, <c>"2"</c>) selects the item
///         at that zero-based position.</item>
///   <item><b>Exact text</b> — the identifier is matched against each item's
///         <see cref="object.ToString"/> representation. An exact, case-insensitive match wins.</item>
///   <item><b>Partial text</b> — when no exact match is found, the identifier is treated as a
///         substring. If exactly one item contains the substring the selection proceeds; if two or
///         more items contain it the result is <see cref="FailureReason.LocatorAmbiguous"/> so that
///         callers are never silently wrong.</item>
///   <item><b>Inner locator</b> — not supported by this L0 strategy; returns
///         <see cref="FailureReason.PatternNotSupported"/> so the selector can escalate.</item>
/// </list>
///
/// Target controls:
/// <list type="bullet">
///   <item><see cref="ListBox"/> / <see cref="ListView"/> — sets <see cref="Selector.SelectedItem"/>.</item>
///   <item><see cref="ComboBox"/> — sets <see cref="Selector.SelectedItem"/>; works for both
///         editable and non-editable ComboBox.</item>
///   <item>Any other <see cref="Selector"/> subclass — sets <see cref="Selector.SelectedItem"/>.</item>
/// </list>
///
/// Virtualized lists (M2-04b): this strategy only handles non-virtualized paths. When the
/// target uses a virtualizing panel the item may not be in the Items collection in a form
/// that allows direct assignment; the caller is expected to scroll-materialise the item first
/// (M2-04b).
///
/// Gate: <see cref="InputIntentKind.SelectItem"/> requires mutation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class SelectItemStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L0;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when the target is a <see cref="Selector"/>
    /// (ListBox, ComboBox, etc.) and the intent kind is <see cref="InputIntentKind.SelectItem"/>.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.SelectItem)
        {
            return false;
        }

        return target is Selector;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        if (target is not Selector selector)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L0,
            };
        }

        var identifier = GetArgument(intent, "identifier") ?? string.Empty;

        // Resolve the index from the identifier.
        var resolveResult = ResolveItemIndex(selector, identifier);
        if (!resolveResult.Success)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = resolveResult.FailureReason,
                ChosenTier = InputTier.L0,
            };
        }

        var index = resolveResult.Index;
        var previousIndex = selector.SelectedIndex;
        var previousValue = previousIndex >= 0 && previousIndex < selector.Items.Count
            ? selector.Items[previousIndex]?.ToString()
            : null;

        // Apply the selection via SetValue to go through the DP change notification path.
        selector.SetValue(Selector.SelectedIndexProperty, index);

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SelectItem({selector.GetType().Name}): index={index}, previous={previousIndex}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = previousValue,
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Item resolution
    // -------------------------------------------------------------------------

    private static (bool Success, int Index, FailureReason FailureReason) ResolveItemIndex(
        Selector selector,
        string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return (false, -1, FailureReason.ElementNotFound);
        }

        // 1. Try numeric index.
        if (int.TryParse(identifier, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var numericIndex))
        {
            if (numericIndex < 0 || numericIndex >= selector.Items.Count)
            {
                return (false, -1, FailureReason.ElementNotFound);
            }

            return (true, numericIndex, default);
        }

        // 2. Try exact text match (case-insensitive).
        var exactMatches = new List<int>();
        var partialMatches = new List<int>();

        for (var i = 0; i < selector.Items.Count; i++)
        {
            var itemText = selector.Items[i]?.ToString() ?? string.Empty;

            if (string.Equals(itemText, identifier, StringComparison.OrdinalIgnoreCase))
            {
                exactMatches.Add(i);
            }
            else if (itemText.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                partialMatches.Add(i);
            }
        }

        // Exact match: if more than one exact match, treat as ambiguous.
        if (exactMatches.Count == 1)
        {
            return (true, exactMatches[0], default);
        }

        if (exactMatches.Count > 1)
        {
            return (false, -1, FailureReason.LocatorAmbiguous);
        }

        // 3. Partial text match: only if exactly one item contains the substring.
        if (partialMatches.Count == 1)
        {
            return (true, partialMatches[0], default);
        }

        if (partialMatches.Count > 1)
        {
            return (false, -1, FailureReason.LocatorAmbiguous);
        }

        return (false, -1, FailureReason.ElementNotFound);
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
