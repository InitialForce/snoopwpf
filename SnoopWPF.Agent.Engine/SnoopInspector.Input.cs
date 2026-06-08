// SnoopInspector.Input.cs
// FX6-A7: User input simulation methods.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;
using SnoopWPF.Agent.Engine.StateDelta;
using SnoopWPF.Agent.Engine.Sync;

/// <content/>
public sealed partial class SnoopInspector
{
    // ── M2-04a/M2-04b: wpf_select_item (L0 — non-virtualized + virtualized scroll) ──────────

    /// <summary>
    /// Maximum number of scroll-materialise iterations before giving up for virtualised lists.
    /// </summary>
    private const int VirtualizedScrollMaxIterations = 20;

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(identifier))
        {
            throw new ArgumentException("identifier must not be null or empty.", nameof(identifier));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_select_item.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.Controls.Primitives.Selector selector)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Resolve item index from identifier.
            int resolvedIndex;
            FailureReason selectFailureReason;
            if (!ResolveItemIndex(selector, identifier, out resolvedIndex, out selectFailureReason))
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = selectFailureReason,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(selectFailureReason, null),
                };
            }

            // ── M2-04b: virtualised path ─────────────────────────────────────────
            // When the selector uses a VirtualizingStackPanel, the container for
            // off-screen items may not yet be materialised. Scroll the item into
            // view first; the panel materialises containers on demand. We pump the
            // Dispatcher between scroll requests to allow WPF layout to run.
            // Budget: VirtualizedScrollMaxIterations attempts before we give up.
            if (IsVirtualizingSelector(selector) &&
                !IsItemContainerMaterialized(selector, resolvedIndex))
            {
                var materialized = ScrollMaterializeItem(selector, resolvedIndex);
                if (!materialized)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[SnoopWPF.Agent] SelectItem({selector.GetType().Name}): item at index {resolvedIndex} " +
                        $"not materialized after {VirtualizedScrollMaxIterations} scroll iterations — " +
                        "returning ElementOutsideViewport.");

                    return new StateDeltaDto
                    {
                        Success = false,
                        ElementVisible = true,
                        StateChanged = false,
                        FailureReason = FailureReason.ElementOutsideViewport,
                        Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.ElementOutsideViewport, null),
                    };
                }
            }

            var previousIndex = selector.SelectedIndex;
            // ADV-PI: Items[idx].ToString() is ViewModel data — guard injection boundary.
            var previousValue = previousIndex >= 0 && previousIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[previousIndex]?.ToString())
                : null;

            // FX2-C8: SetCurrentValue preserves TwoWay bindings on SelectedIndex.
            selector.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, resolvedIndex);

            var newIndex = selector.SelectedIndex;
            // ADV-PI: Items[idx].ToString() is ViewModel data — guard injection boundary.
            var newValue = newIndex >= 0 && newIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[newIndex]?.ToString())
                : null;
            var stateChanged = previousIndex != newIndex;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SelectItem({selector.GetType().Name}): nodeId={nodeId}, index={resolvedIndex}, stateChanged={stateChanged}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <summary>
    /// Returns the ItemsHost panel for an <see cref="System.Windows.Controls.ItemsControl"/>
    /// using reflection (ItemsHost is an internal property in WPF).
    /// </summary>
    private static System.Windows.Controls.Panel? GetItemsHost(System.Windows.Controls.ItemsControl itemsControl)
    {
        var prop = typeof(System.Windows.Controls.ItemsControl)
            .GetProperty("ItemsHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(itemsControl) as System.Windows.Controls.Panel;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="selector"/> is backed by a
    /// <see cref="System.Windows.Controls.VirtualizingStackPanel"/> (i.e. UI-virtualisation
    /// is active and item containers may not yet be materialised).
    /// </summary>
    private static bool IsVirtualizingSelector(System.Windows.Controls.Primitives.Selector selector)
    {
        // VirtualizingPanel.IsVirtualizing attached DP is the canonical flag.
        var isVirtualizing = (bool)selector.GetValue(
            System.Windows.Controls.VirtualizingPanel.IsVirtualizingProperty);
        if (!isVirtualizing)
        {
            return false;
        }

        // Confirm the items host is actually a VirtualizingStackPanel.
        if (selector is System.Windows.Controls.ItemsControl itemsControl)
        {
            var panel = GetItemsHost(itemsControl);
            return panel is System.Windows.Controls.VirtualizingStackPanel;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the item container for
    /// <paramref name="index"/> is already materialised (the generator's status
    /// for that position is <see cref="System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated"/>
    /// and <see cref="System.Windows.Controls.Primitives.ItemContainerGenerator.ContainerFromIndex"/>
    /// returns a non-null element).
    /// </summary>
    private static bool IsItemContainerMaterialized(
        System.Windows.Controls.Primitives.Selector selector,
        int index)
    {
        var generator = selector.ItemContainerGenerator;
        if (generator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            return false;
        }

        return generator.ContainerFromIndex(index) != null;
    }

    /// <summary>
    /// Attempts to scroll-materialise the container for item at <paramref name="index"/>
    /// within <paramref name="selector"/> by issuing up to
    /// <see cref="VirtualizedScrollMaxIterations"/> <c>BringIndexIntoView</c> / <c>ScrollIntoView</c>
    /// calls and pumping the Dispatcher between each.
    /// Returns <see langword="true"/> when the container is materialised before the
    /// budget is exhausted.
    /// </summary>
    private static bool ScrollMaterializeItem(
        System.Windows.Controls.Primitives.Selector selector,
        int index)
    {
        for (var iteration = 0; iteration < VirtualizedScrollMaxIterations; iteration++)
        {
            // Request the panel to bring the item into view (materialises its container).
            if (selector is System.Windows.Controls.ItemsControl itemsControl)
            {
                itemsControl.UpdateLayout();

                // VirtualizingStackPanel.BringIndexIntoViewPublic is internal; use the
                // public ScrollViewer.ScrollIntoView path via ItemsControl.
                var panel = GetItemsHost(itemsControl) as System.Windows.Controls.VirtualizingStackPanel;
                if (panel != null)
                {
                    // Calling BringIndexIntoView on VirtualizingStackPanel scrolls the panel
                    // without requiring the container to already exist.
                    panel.BringIndexIntoViewPublic(index);
                }

                // Also ask the ScrollViewer (if present) to ensure visibility.
                if (selector is System.Windows.Controls.ListBox listBox)
                {
                    if (index >= 0 && index < listBox.Items.Count)
                    {
                        listBox.ScrollIntoView(listBox.Items[index]);
                    }
                }

                // Pump layout so the panel can materialise containers.
                itemsControl.UpdateLayout();
            }

            if (IsItemContainerMaterialized(selector, index))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SelectItemAsync(nodeId, identifier, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a zero-based item index from an <paramref name="identifier"/> string
    /// within the given <paramref name="selector"/>'s Items collection.
    /// Returns <see langword="true"/> when a unique match is found.
    /// </summary>
    private static bool ResolveItemIndex(
        System.Windows.Controls.Primitives.Selector selector,
        string identifier,
        out int resolvedIndex,
        out FailureReason failureReason)
    {
        // 1. Try numeric index.
        int numericIndex;
        if (int.TryParse(identifier, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out numericIndex))
        {
            if (numericIndex < 0 || numericIndex >= selector.Items.Count)
            {
                resolvedIndex = -1;
                failureReason = FailureReason.ElementNotFound;
                return false;
            }

            resolvedIndex = numericIndex;
            failureReason = default(FailureReason);
            return true;
        }

        // 2. Exact text match (case-insensitive).
        var exactMatches = new System.Collections.Generic.List<int>();
        var partialMatches = new System.Collections.Generic.List<int>();

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

        if (exactMatches.Count == 1)
        {
            resolvedIndex = exactMatches[0];
            failureReason = default(FailureReason);
            return true;
        }

        if (exactMatches.Count > 1)
        {
            resolvedIndex = -1;
            failureReason = FailureReason.LocatorAmbiguous;
            return false;
        }

        // 3. Partial text match — unambiguous substring only.
        if (partialMatches.Count == 1)
        {
            resolvedIndex = partialMatches[0];
            failureReason = default(FailureReason);
            return true;
        }

        if (partialMatches.Count > 1)
        {
            resolvedIndex = -1;
            failureReason = FailureReason.LocatorAmbiguous;
            return false;
        }

        resolvedIndex = -1;
        failureReason = FailureReason.ElementNotFound;
        return false;
    }

    // ── M2-03: wpf_set_check_state ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(state))
        {
            throw new ArgumentException("state must not be null or empty.", nameof(state));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_check_state.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Reject bare ToggleButton (not CheckBox/RadioButton) — suggest wpf_toggle.
            if (depObj is System.Windows.Controls.Primitives.ToggleButton
                and not System.Windows.Controls.CheckBox
                and not System.Windows.Controls.RadioButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = new Contracts.Dtos.SuggestionDto
                    {
                        Tool = "wpf_toggle",
                        Args = new System.Collections.Generic.List<Contracts.Dtos.NameValuePairDto>
                        {
                            new() { Name = "nodeId", Value = nodeId },
                            new() { Name = "hint", Value = "Use wpf_toggle for bare ToggleButton controls." },
                        },
                    },
                };
            }

            // Only CheckBox and RadioButton are supported.
            if (depObj is not System.Windows.Controls.Primitives.ToggleButton toggleButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Parse the desired state.
            bool? desiredState;
            if (string.Equals(state, "checked", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = true;
            }
            else if (string.Equals(state, "unchecked", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = false;
            }
            else if (string.Equals(state, "indeterminate", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = null;
            }
            else
            {
                throw new SnoopException(
                    SnoopErrorCode.TypeConversionFailed,
                    $"Invalid state value '{state}'. Expected \"checked\", \"unchecked\", or \"indeterminate\".",
                    targetId: nodeId);
            }

            var previousRaw = toggleButton.IsChecked;
            var previousValue = FormatChecked(previousRaw);

            // FX2-C8: SetCurrentValue preserves TwoWay bindings on IsChecked.
            toggleButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, desiredState);

            var newRaw = toggleButton.IsChecked;
            var newValue = FormatChecked(newRaw);
            var stateChanged = previousRaw != newRaw;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetCheckState({depObj.GetType().Name}): nodeId={nodeId}, previous={previousValue}, new={newValue}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetCheckStateAsync(nodeId, state, ct).ConfigureAwait(false);
    }

    private static string FormatChecked(bool? value) => value switch
    {
        true => "checked",
        false => "unchecked",
        null => "indeterminate",
    };

    // ── M2-02: wpf_set_text_value ─────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_text_value.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // ── TextBox ──────────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.TextBox textBox)
            {
                var previousValue = textBox.Text;

                // FX2-C8: SetCurrentValue preserves TwoWay bindings on TextBox.Text.
                // SetValue would write at Local priority, silently breaking any binding
                // from ViewModel → TextBox.Text after the first mutation.
                textBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, value);
                var newValue = textBox.Text;
                var stateChanged = !string.Equals(previousValue, newValue, System.StringComparison.Ordinal);

                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] SetTextValue(TextBox): nodeId={nodeId}, stateChanged={stateChanged}");

                if (!stateChanged)
                {
                    return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                    {
                        ElementVisible = true,
                        PreviousValue = previousValue,
                        NewValue = newValue,
                        ChosenTier = InputTier.L0,
                    };
                }

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            // ── PasswordBox ──────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.PasswordBox passwordBox)
            {
                // SensitiveText (S3): never log or return the actual password value.
                const string redactedMarker = "[REDACTED]";

                // AllowSensitiveRetention gate: only return newValue when explicitly permitted.
                var allowRetention = this.options.AllowSensitiveRetention;

                System.Diagnostics.Trace.WriteLine(
                    "[SnoopWPF.Agent] SetTextValue(PasswordBox): value=<redacted>");

                // PasswordBox.Password is a CLR property backed by SecureString, not a standard DP.
                passwordBox.Password = value;

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = redactedMarker,
                    NewValue = allowRetention ? value : redactedMarker,
                    ChosenTier = InputTier.L0,
                };
            }

            // ── RichTextBox ──────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.RichTextBox richTextBox)
            {
                var startPointer = richTextBox.Document.ContentStart;
                var endPointer = richTextBox.Document.ContentEnd;
                var previousValue = new System.Windows.Documents.TextRange(startPointer, endPointer).Text ?? string.Empty;

                richTextBox.Document = new System.Windows.Documents.FlowDocument(
                    new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(value)));

                var newStartPointer = richTextBox.Document.ContentStart;
                var newEndPointer = richTextBox.Document.ContentEnd;
                var newValue = new System.Windows.Documents.TextRange(newStartPointer, newEndPointer).Text ?? string.Empty;
                var stateChanged = !string.Equals(previousValue, newValue, System.StringComparison.Ordinal);

                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] SetTextValue(RichTextBox): nodeId={nodeId}, stateChanged={stateChanged}");

                if (!stateChanged)
                {
                    return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                    {
                        ElementVisible = true,
                        PreviousValue = previousValue,
                        NewValue = newValue,
                        ChosenTier = InputTier.L0,
                    };
                }

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            // Not a supported text control.
            return new StateDeltaDto
            {
                Success = false,
                ElementVisible = true,
                StateChanged = false,
                FailureReason = FailureReason.PatternNotSupported,
                Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetTextValueAsync(nodeId, value, ct).ConfigureAwait(false);
    }

    // ── M2-16: wpf_set_slider_value ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetSliderValueAsync(string nodeId, double value, bool normalized, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_slider_value.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.Controls.Primitives.RangeBase rangeBase)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = target is System.Windows.FrameworkElement,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            var previousValue = rangeBase.Value;

            double targetValue;
            if (normalized)
            {
                var fraction = Math.Max(0.0, Math.Min(1.0, value));
                var min = rangeBase.Minimum;
                var max = rangeBase.Maximum;
                var range = max - min;
                targetValue = range <= 0.0 ? min : min + (fraction * range);
            }
            else
            {
                targetValue = Math.Max(rangeBase.Minimum, Math.Min(rangeBase.Maximum, value));
            }

            // FX2-C8: SetCurrentValue preserves TwoWay bindings on RangeBase.Value (Slider etc.).
            rangeBase.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, targetValue);

            var newValue = rangeBase.Value;
            var stateChanged = Math.Abs(previousValue - newValue) > double.Epsilon;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetSliderValue({rangeBase.GetType().Name}): " +
                $"normalized={normalized}, input={value}, target={targetValue}, stateChanged={stateChanged}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = newValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValue = newValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetSliderValueAsync(WpfLocator locator, double value, bool normalized, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetSliderValueAsync(nodeId, value, normalized, ct).ConfigureAwait(false);
    }

    // ── M2-01: wpf_execute_command ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled (L0 execute = mutation).
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_execute_command.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Resolve Command DP (ButtonBase.CommandProperty is the canonical L0 DP).
            var command = depObj.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)
                          as System.Windows.Input.ICommand;

            if (command is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Capture previousValue: window count acts as a coarse state proxy.
            var windowCountBefore = System.Windows.Application.Current?.Windows.Count ?? 0;
            var previousValue = windowCountBefore.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Resolve optional CommandParameter.
            var commandParameter = depObj.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandParameterProperty);

            // CanExecute gate.
            if (!command.CanExecute(commandParameter))
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.CannotExecuteCommand,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.CannotExecuteCommand, null),
                    PreviousValue = previousValue,
                };
            }

            // Execute the command.
            command.Execute(commandParameter);

            // Compute stateChanged: compare window count before vs after.
            var windowCountAfter = System.Windows.Application.Current?.Windows.Count ?? 0;
            var treeVersionDelta = windowCountAfter - windowCountBefore;
            var stateChanged = treeVersionDelta != 0;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ExecuteCommand: nodeId={nodeId}, windowDelta={treeVersionDelta}");

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = stateChanged,
                TreeVersionDelta = treeVersionDelta,
                PreviousValue = previousValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ExecuteCommandAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-05: wpf_click ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>StateChanged semantics (PRD §7.6):</b> <c>StateChanged=true</c> in the success path
    /// reflects that <c>IInvokeProvider.Invoke()</c> completed without throwing — it does
    /// NOT guarantee the target control's state actually changed. Invoke() is inherently
    /// fire-and-forget with no observable post-state. Use <c>wpf_inspect_element</c>
    /// afterwards if you need to verify the outcome.
    /// </para>
    /// </remarks>
    public Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_click.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Obtain automation peer and IInvokeProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var invokeProvider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.Invoke)
                as System.Windows.Automation.Provider.IInvokeProvider;

            if (invokeProvider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Detect whether a Command is bound — if so, suggest wpf_execute_command (L0 preferred).
            var hasCommandBound =
                uiElement.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)
                is System.Windows.Input.ICommand;

            // FX4-C7-retry: narrow to ElementNotEnabledException — the pattern IS supported
            // but the element is disabled. PatternNotSupported is reserved for absent patterns.
            // Note: StateChanged reflects whether Invoke() completed without error; true does NOT
            // imply the target control's state actually changed. Use wpf_inspect_element afterwards
            // if post-invoke observation is required (PRD §7.6).
            try
            {
                invokeProvider.Invoke();
            }
            catch (System.Windows.Automation.ElementNotEnabledException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ClickAsync: nodeId={nodeId}, element disabled: {ex.GetType().Name}");
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.ElementDisabled,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.ElementDisabled, null),
                };
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ClickAsync: nodeId={nodeId}, UIA Invoke threw unexpected InvalidOperationException: {ex.GetType().Name}: {ex.Message}");
                throw;
            }

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ClickAsync: nodeId={nodeId}, hasCommandBound={hasCommandBound}");

            // When a Command is bound, attach a hint suggesting wpf_execute_command (L0).
            SuggestionDto? suggestion = hasCommandBound
                ? new SuggestionDto
                {
                    Tool = "wpf_execute_command",
                    Args = new System.Collections.Generic.List<NameValuePairDto>
                    {
                        new() { Name = "nodeId", Value = nodeId },
                        new()
                        {
                            Name = "hint",
                            Value = "Element has a Command bound; prefer wpf_execute_command (L0) over wpf_click (L1).",
                        },
                    },
                }
                : null;

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
                Suggestion = suggestion,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ClickAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-06: wpf_toggle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_toggle.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Reject CheckBox and RadioButton — suggest wpf_set_check_state (L0 preferred for deterministic state).
            if (uiElement is System.Windows.Controls.CheckBox or System.Windows.Controls.RadioButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = new SuggestionDto
                    {
                        Tool = "wpf_set_check_state",
                        Args = new System.Collections.Generic.List<NameValuePairDto>
                        {
                            new() { Name = "nodeId", Value = nodeId },
                            new()
                            {
                                Name = "hint",
                                Value = "Use wpf_set_check_state (L0) for CheckBox/RadioButton to specify a deterministic target state.",
                            },
                        },
                    },
                };
            }

            // Obtain automation peer and IToggleProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var toggleProvider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.Toggle)
                as System.Windows.Automation.Provider.IToggleProvider;

            if (toggleProvider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // FX4-C7-retry: capture pre-call state for post-toggle comparison (mirrors Expand/Collapse).
            var previousToggleState = toggleProvider.ToggleState;

            // FX4-C7-retry: narrow to ElementNotEnabledException — element is disabled, pattern is present.
            try
            {
                toggleProvider.Toggle();
            }
            catch (System.Windows.Automation.ElementNotEnabledException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ToggleAsync: nodeId={nodeId}, element disabled: {ex.GetType().Name}");
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.ElementDisabled,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.ElementDisabled, null),
                };
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ToggleAsync: nodeId={nodeId}, UIA Toggle threw unexpected InvalidOperationException: {ex.GetType().Name}: {ex.Message}");
                throw;
            }

            var newToggleState = toggleProvider.ToggleState;
            var toggleStateChanged = previousToggleState != newToggleState;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ToggleAsync: nodeId={nodeId}, type={uiElement.GetType().Name}, " +
                $"prev={previousToggleState}, new={newToggleState}");

            if (!toggleStateChanged)
            {
                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = false,
                    ChosenTier = InputTier.L1,
                    FailureReason = FailureReason.StateUnchanged,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ToggleAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-07: wpf_expand_collapse ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(action) ||
            (!string.Equals(action, "expand", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(action, "collapse", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("action must be \"expand\" or \"collapse\".", nameof(action));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_expand_collapse.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            WarnIfModallyBlocked(target);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Obtain automation peer and IExpandCollapseProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var provider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.ExpandCollapse)
                as System.Windows.Automation.Provider.IExpandCollapseProvider;

            if (provider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            bool expand = string.Equals(action, "expand", StringComparison.OrdinalIgnoreCase);

            // FX2 (META-M1): capture the pre-call ExpandCollapseState so we can detect
            // no-op requests (e.g. Expand() on an already-expanded node) and return
            // StateChanged=false + STATE_UNCHANGED instead of misreporting a mutation.
            var previousState = provider.ExpandCollapseState;

            // FX4-C7-retry: narrow to ElementNotEnabledException — element is disabled, pattern is present.
            try
            {
                if (expand)
                {
                    provider.Expand();
                }
                else
                {
                    provider.Collapse();
                }
            }
            catch (System.Windows.Automation.ElementNotEnabledException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ExpandCollapseAsync: nodeId={nodeId}, element disabled: {ex.GetType().Name}");
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.ElementDisabled,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.ElementDisabled, null),
                };
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] ExpandCollapseAsync: nodeId={nodeId}, UIA {action} threw unexpected InvalidOperationException: {ex.GetType().Name}: {ex.Message}");
                throw;
            }

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ExpandCollapseAsync: nodeId={nodeId}, action={action}, type={uiElement.GetType().Name}");

            var newState = provider.ExpandCollapseState;
            var stateChanged = previousState != newState;

            if (!stateChanged)
            {
                // FX-M10 / PRD §7.6: Success=true + StateChanged=false → STATE_UNCHANGED.
                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = false,
                    ChosenTier = InputTier.L1,
                    FailureReason = FailureReason.StateUnchanged,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ExpandCollapseAsync(nodeId, action, ct).ConfigureAwait(false);
    }
}
