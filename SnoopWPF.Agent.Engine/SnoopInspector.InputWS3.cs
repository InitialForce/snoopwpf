// SnoopInspector.InputWS3.cs
// WS3-02/03/04/06: wpf_double_click, wpf_select_item_by_scroll,
//                  wpf_select_item_by_index, wpf_get_list_items.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Diagnostics;
using SnoopWPF.Agent.Engine.Infrastructure;
using SnoopWPF.Agent.Engine.StateDelta;

/// <content/>
public sealed partial class SnoopInspector
{
    // ── WS3-02: wpf_double_click ─────────────────────────────────────────────

    /// <summary>
    /// Controls that reliably respond to the routed MouseDoubleClick RaiseEvent path
    /// (primary path sufficient; no SendInput fallback needed).
    /// </summary>
    private static readonly System.Type[] KnownDoubleClickFriendlyTypes =
    {
        typeof(System.Windows.Controls.ListBoxItem),
        typeof(System.Windows.Controls.Primitives.ButtonBase),
        typeof(System.Windows.Controls.MenuItem),
    };

    /// <inheritdoc/>
    public Task<StateDeltaDto> DoubleClickAsync(string nodeId, CancellationToken ct)
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
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_double_click.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not UIElement uiElement)
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

            // Primary path: raise MouseDoubleClick routed event (ClickCount=2).
            bool handledByPrimary = RaiseDoubleClickRoutedEvent(uiElement);
            bool isKnownFriendly = IsKnownDoubleClickFriendly(uiElement);

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] DoubleClickAsync: nodeId={nodeId}, type={uiElement.GetType().Name}, " +
                $"primaryHandled={handledByPrimary}, isKnownFriendly={isKnownFriendly}");

            if (!handledByPrimary && !isKnownFriendly)
            {
                // Fallback: Win32 SendInput mouse sequence (LEFTDOWN+LEFTUP × 2).
                NativeMethods.SendInputDoubleClick(uiElement);

                SnoopAgentContext.AddWarning(
                    "DOUBLE_CLICK_FALLBACK",
                    $"double-click used SendInput fallback (control: {uiElement.GetType().Name})");

                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] DoubleClickAsync: nodeId={nodeId}, SendInput fallback used");
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                // L1 for routed-event path; SendInput fallback is still L1 in current tier model.
                ChosenTier = InputTier.L1,
            };
        }, ct);
    }

    /// <summary>
    /// Raises the WPF MouseLeftButtonDown + MouseLeftButtonUp pair twice
    /// (ClickCount=2 on the second pair) and Control.MouseDoubleClickEvent.
    /// Returns <see langword="true"/> when any handler marks the event as Handled.
    /// </summary>
    private static bool RaiseDoubleClickRoutedEvent(UIElement element)
    {
        // First click pair (ClickCount=1).
        var args1Down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = element,
        };
        element.RaiseEvent(args1Down);

        var args1Up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = element,
        };
        element.RaiseEvent(args1Up);

        // Second click pair (ClickCount=2 → double-click).
        var args2Down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = element,
        };
        SetMouseButtonClickCount(args2Down, 2);
        element.RaiseEvent(args2Down);

        var args2Up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = element,
        };
        SetMouseButtonClickCount(args2Up, 2);
        element.RaiseEvent(args2Up);

        // Raise the high-level MouseDoubleClick event (for controls that listen there).
        var dblClickArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Control.MouseDoubleClickEvent,
            Source = element,
        };
        SetMouseButtonClickCount(dblClickArgs, 2);
        element.RaiseEvent(dblClickArgs);

        return dblClickArgs.Handled || args2Down.Handled || args2Up.Handled;
    }

    /// <summary>
    /// Sets <see cref="MouseButtonEventArgs.ClickCount"/> via reflection (the property has
    /// no public setter — WPF populates it from the underlying <c>_count</c> field).
    /// </summary>
    private static void SetMouseButtonClickCount(MouseButtonEventArgs args, int count)
    {
        var field = typeof(MouseButtonEventArgs).GetField(
            "_count",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field is not null)
        {
            field.SetValue(args, count);
        }
        else
        {
            // Future WPF versions may expose a public setter.
            var prop = typeof(MouseButtonEventArgs).GetProperty(
                "ClickCount",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            prop?.SetValue(args, count);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="element"/> is one of the
    /// well-known types that reliably handle the routed double-click event.
    /// </summary>
    private static bool IsKnownDoubleClickFriendly(UIElement element)
    {
        var type = element.GetType();
        foreach (var friendly in KnownDoubleClickFriendlyTypes)
        {
            if (friendly.IsAssignableFrom(type))
            {
                return true;
            }
        }

        return false;
    }

    // ── WS3-03: wpf_select_item_by_scroll ────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemByScrollAsync(string nodeId, int targetIndex, CancellationToken ct)
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
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_select_item_by_scroll.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not Selector selector)
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

            if (targetIndex < 0 || targetIndex >= selector.Items.Count)
            {
                throw new SnoopException(
                    SnoopErrorCode.InvalidArgument,
                    $"targetIndex {targetIndex} is out of range (Items.Count={selector.Items.Count}).",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.InvalidArgument });
            }

            // Scroll-materialize the container before selection (handles virtualized lists).
            if (!IsItemContainerMaterialized(selector, targetIndex))
            {
                var materialized = ScrollMaterializeItem(selector, targetIndex);
                if (!materialized)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[SnoopWPF.Agent] SelectItemByScrollAsync({selector.GetType().Name}): " +
                        $"item at index {targetIndex} not materialized after scroll — returning ElementOutsideViewport.");

                    return new StateDeltaDto
                    {
                        Success = false,
                        ElementVisible = true,
                        StateChanged = false,
                        FailureReason = FailureReason.ElementOutsideViewport,
                        Suggestion = FailureReasonDescriptor.Suggest(FailureReason.ElementOutsideViewport, null),
                    };
                }
            }

            var previousIndex = selector.SelectedIndex;
            var previousValue = previousIndex >= 0 && previousIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[previousIndex]?.ToString())
                : null;

            // FX2-C8: SetCurrentValue preserves TwoWay bindings on SelectedIndex.
            selector.SetCurrentValue(Selector.SelectedIndexProperty, targetIndex);

            var newIndex = selector.SelectedIndex;
            var newValue = newIndex >= 0 && newIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[newIndex]?.ToString())
                : null;
            var stateChanged = previousIndex != newIndex;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SelectItemByScrollAsync({selector.GetType().Name}): " +
                $"nodeId={nodeId}, index={targetIndex}, stateChanged={stateChanged}");

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

    // ── WS3-04: wpf_select_item_by_index ─────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemByIndexAsync(string nodeId, int index, CancellationToken ct)
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
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_select_item_by_index.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not Selector selector)
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

            if (index < 0 || index >= selector.Items.Count)
            {
                throw new SnoopException(
                    SnoopErrorCode.InvalidArgument,
                    $"index {index} is out of range (Items.Count={selector.Items.Count}).",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.InvalidArgument });
            }

            var previousIndex = selector.SelectedIndex;
            var previousValue = previousIndex >= 0 && previousIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[previousIndex]?.ToString())
                : null;

            // FX2-C8: SetCurrentValue preserves TwoWay bindings on SelectedIndex.
            selector.SetCurrentValue(Selector.SelectedIndexProperty, index);

            var newIndex = selector.SelectedIndex;
            var newValue = newIndex >= 0 && newIndex < selector.Items.Count
                ? PromptInjectionGuard.Quote(selector.Items[newIndex]?.ToString())
                : null;
            var stateChanged = previousIndex != newIndex;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SelectItemByIndexAsync({selector.GetType().Name}): " +
                $"nodeId={nodeId}, index={index}, stateChanged={stateChanged}");

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

    // ── WS3-06: wpf_get_list_items ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<List<ListItemDto>> GetListItemsAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not ItemsControl itemsControl)
            {
                throw new SnoopException(
                    SnoopErrorCode.InvalidArgument,
                    $"Element {nodeId} (type={target?.GetType().Name ?? "null"}) is not an ItemsControl. " +
                    "Use wpf_get_children to explore the visual tree.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.InvalidArgument });
            }

            var result = new List<ListItemDto>(itemsControl.Items.Count);
            var generator = itemsControl.ItemContainerGenerator;

            // Determine which index is currently selected (for Selector subclasses).
            int selectedIndex = itemsControl is Selector sel ? sel.SelectedIndex : -1;

            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                // Skip virtualized (non-materialized) containers.
                var container = generator.ContainerFromIndex(i) as DependencyObject;
                if (container is null)
                {
                    continue;
                }

                // Register the container so callers get a stable nodeId for follow-up calls.
                var containerNodeId = this.nodeRegistry.GetOrCreateId(container);

                var item = itemsControl.Items[i];
                // ADV-PI: item.ToString() is ViewModel data — sanitize before returning.
                var displayName = PromptInjectionGuard.Quote(item?.ToString() ?? string.Empty);

                // Prefer the container's own IsSelected DP when available.
                bool isSelected;
                if (container is System.Windows.Controls.ListBoxItem lbi)
                {
                    isSelected = lbi.IsSelected;
                }
                else if (container is TreeViewItem tvi)
                {
                    isSelected = tvi.IsSelected;
                }
                else
                {
                    isSelected = i == selectedIndex;
                }

                result.Add(new ListItemDto
                {
                    Index = i,
                    NodeId = containerNodeId,
                    DisplayName = displayName,
                    IsSelected = isSelected,
                });
            }

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] GetListItemsAsync({itemsControl.GetType().Name}): " +
                $"nodeId={nodeId}, realized={result.Count}/{itemsControl.Items.Count}");

            return result;
        }, ct);
    }
}

// ── Win32 P/Invoke for double-click SendInput fallback ────────────────────────
// Placed in a separate non-nested internal static class to satisfy CA1060
// (PInvokes must be in a class named NativeMethods or similar).

/// <summary>
/// Win32 SendInput helpers for the wpf_double_click SendInput fallback path.
/// </summary>
internal static class NativeMethods
{
    private const uint InputMouseType = 0;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;

#pragma warning disable SA1307 // Field names in P/Invoke structs must match Win32 conventions.
#pragma warning disable SA1201 // Struct members can be in Win32 order.

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307", Justification = "Win32 P/Invoke field names")]
    private struct MouseInput
    {
        internal int dx;
        internal int dy;
        internal uint mouseData;
        internal uint dwFlags;
        internal uint time;
        internal nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307", Justification = "Win32 P/Invoke field names")]
    private struct Input
    {
        internal uint type;
        internal MouseInput mi;
    }

#pragma warning restore SA1307
#pragma warning restore SA1201

    [DllImport("user32.dll", SetLastError = true)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313", Justification = "Win32 P/Invoke parameter names")]
    [SuppressMessage("Microsoft.Interoperability", "CA1060", Justification = "NativeMethods class")]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313", Justification = "Win32 P/Invoke parameter names")]
    [SuppressMessage("Microsoft.Interoperability", "CA1060", Justification = "NativeMethods class")]
    private static extern bool SetCursorPos(int X, int Y);

    /// <summary>
    /// Performs a Win32 SendInput double-click at the center of <paramref name="element"/>'s
    /// bounding box. Used as fallback when the routed event primary path is ineffective.
    /// </summary>
    internal static void SendInputDoubleClick(System.Windows.UIElement element)
    {
        int x, y;
        try
        {
            var bounds = VisualTreeHelper.GetDescendantBounds(element);
            if (bounds == Rect.Empty)
            {
                bounds = new Rect(0, 0, 1, 1);
            }

            var centerLocal = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            var centerScreen = element.PointToScreen(centerLocal);
            x = (int)centerScreen.X;
            y = (int)centerScreen.Y;
        }
        catch
        {
            // Element not in visual tree — skip fallback silently.
            System.Diagnostics.Trace.WriteLine(
                "[SnoopWPF.Agent] DoubleClickAsync: SendInput fallback skipped — element not in visual tree");
            return;
        }

        SetCursorPos(x, y);

        var inputs = new[]
        {
            new Input { type = InputMouseType, mi = new MouseInput { dwFlags = MouseEventFLeftDown } },
            new Input { type = InputMouseType, mi = new MouseInput { dwFlags = MouseEventFLeftUp } },
            new Input { type = InputMouseType, mi = new MouseInput { dwFlags = MouseEventFLeftDown } },
            new Input { type = InputMouseType, mi = new MouseInput { dwFlags = MouseEventFLeftUp } },
        };

        // CA1806: return value is nInputs accepted; we don't need to verify it for best-effort fallback.
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }
}
