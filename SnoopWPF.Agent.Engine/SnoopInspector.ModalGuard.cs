// SnoopInspector.ModalGuard.cs
// Emits a MODAL_BLOCKED warning when an interaction targets an element whose
// window is disabled by an open modal dialog.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SnoopWPF.Agent.Contracts.Diagnostics;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <content/>
public sealed partial class SnoopInspector
{
    /// <summary>
    /// Emits a <c>MODAL_BLOCKED</c> warning when <paramref name="target"/> lives in a window
    /// that is currently disabled by a modal dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF's <see cref="Window.ShowDialog"/> disables every other top-level window on the
    /// thread via the Win32 <c>EnableWindow(hwnd, false)</c> call. Interacting with a control
    /// behind that disabled window silently no-ops, which otherwise leaves the agent guessing
    /// why a click had no effect. Detecting the disabled owner HWND is more robust and more
    /// universal than matching against a catalogue of known dialogs: it works for any modal,
    /// including ones the broker has never seen.
    /// </para>
    /// <para>
    /// No-op when the target is inside the active modal (its own window stays enabled), when
    /// the owning window cannot be resolved, or when the window has no HWND yet. Runs on the
    /// dispatcher thread — callers are already inside <c>RunOnDispatcherAsync</c>.
    /// </para>
    /// </remarks>
    internal static void WarnIfModallyBlocked(object target)
    {
        if (target is not DependencyObject dependencyObject)
        {
            return;
        }

        var window = target as Window ?? Window.GetWindow(dependencyObject);
        if (window is null)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (SafeNativeMethods.IsWindowEnabled(hwnd))
        {
            // The target's window accepts input — nothing is blocking it.
            return;
        }

        // The owning window is disabled, so a modal dialog elsewhere is holding input.
        var blocker = FindModalBlockerName(window);

        SnoopAgentContext.AddWarning(
            "MODAL_BLOCKED",
            blocker is null
                ? $"Target is in window '{DescribeWindow(window)}', which is disabled by an open modal dialog. " +
                  "Interaction will not take effect until the dialog is dismissed (e.g. mc_list_dialogs then mc_dismiss_dialogs)."
                : $"Target is in window '{DescribeWindow(window)}', which is disabled by modal dialog '{blocker}'. " +
                  "Interaction will not take effect until the dialog is dismissed (e.g. mc_list_dialogs then mc_dismiss_dialogs).",
            new
            {
                blockedWindow = window.GetType().Name,
                blockingDialog = blocker,
            });
    }

    /// <summary>
    /// Finds a human-readable name for the modal window that is blocking
    /// <paramref name="blockedWindow"/>, i.e. the visible top-level window that is still
    /// enabled while the others are disabled. Prefers a window owned by the blocked window.
    /// Returns <see langword="null"/> when no enabled sibling can be identified.
    /// </summary>
    private static string? FindModalBlockerName(Window blockedWindow)
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        string? candidate = null;
        foreach (Window w in app.Windows)
        {
            if (w is null || ReferenceEquals(w, blockedWindow) || !w.IsVisible)
            {
                continue;
            }

            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero || !SafeNativeMethods.IsWindowEnabled(h))
            {
                continue;
            }

            candidate = DescribeWindow(w);

            // An enabled window owned by the blocked window is almost certainly the modal.
            if (ReferenceEquals(w.Owner, blockedWindow))
            {
                break;
            }
        }

        return candidate;
    }

    private static string DescribeWindow(Window w)
    {
        var title = w.Title;
        // ADV-PI: Window.Title is app-controlled — quote it before embedding in an
        // LLM-facing warning message, matching the trust-boundary handling elsewhere.
        return string.IsNullOrWhiteSpace(title)
            ? w.GetType().Name
            : $"{w.GetType().Name} ({PromptInjectionGuard.Quote(title)})";
    }
}

/// <summary>
/// Win32 P/Invoke for modal-blocked detection. <c>IsWindowEnabled</c> is a read-only,
/// side-effect-free query, so it belongs in a <c>SafeNativeMethods</c> class (CA1060).
/// </summary>
internal static class SafeNativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313", Justification = "Win32 P/Invoke parameter names")]
    internal static extern bool IsWindowEnabled(IntPtr hWnd);
}
