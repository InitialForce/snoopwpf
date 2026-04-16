namespace SnoopWPF.Agent.Server;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

/// <summary>
/// Boot-sequence step 5 (PRD §4.2): precondition smoke tests run once at agent startup.
/// </summary>
internal static class SelfTest
{
    /// <summary>
    /// Smoke-tests that <c>[UnsafeAccessor]</c> private-member binding resolves correctly.
    /// On net8.0+ the accessor is invoked once against a known type; on earlier TFMs this
    /// is a no-op that logs a diagnostic so future L3 work has a clear signal.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown on net8.0+ when the <c>[UnsafeAccessor]</c> binding fails to resolve or
    /// returns an unexpected sentinel value.
    /// </exception>
    internal static void UnsafeAccessorBindings()
    {
#if NET8_0_OR_GREATER
        try
        {
            // Smoke test: read a known private backing-field on SelfTestTarget using
            // [UnsafeAccessor].  SelfTestTarget is internal to this assembly so there
            // is no cross-assembly visibility concern.
            var target = new SelfTestTarget(42);
            int value = GetSelfTestValue(ref target);

            if (value != 42)
            {
                throw new InvalidOperationException(
                    $"UnsafeAccessor self-test returned unexpected value {value} (expected 42). " +
                    "This indicates a runtime binding regression.");
            }

            Trace.TraceInformation("SnoopWPF.Agent SelfTest: UnsafeAccessorBindings passed.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Wrap lower-level binding exceptions so callers always see InvalidOperationException.
            throw new InvalidOperationException(
                "UnsafeAccessor self-test failed with an unexpected exception. " +
                "See inner exception for details.", ex);
        }
#else
        // On earlier TFMs [UnsafeAccessor] is not supported.
        // Log a diagnostic so v2.0 L3 work knows the accessor path was not exercised.
        Trace.TraceInformation(
            "SnoopWPF.Agent SelfTest: UnsafeAccessorBindings — skipped (TFM < net8.0; " +
            "UnsafeAccessor not supported on this runtime).");
#endif
    }

    /// <summary>
    /// Verifies that <c>HwndSource.FromVisual(Application.Current.MainWindow)</c> is resolvable.
    /// Must be called from the WPF dispatcher thread (STA), or will marshal the
    /// <c>MainWindow</c> access via <see cref="Dispatcher.Invoke"/>.
    /// When the process starts headless (no main window yet), registers a one-shot
    /// <see cref="Application.Activated"/> handler and fails the agent if no window appears
    /// within <see cref="HeadlessWindowTimeoutMs"/> milliseconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown immediately when <see cref="Application.Current"/> is null, or after the
    /// headless timeout elapses without a window materialising.
    /// </exception>
    internal static void HwndSourcePresent()
    {
        var app = Application.Current;
        if (app is null)
        {
            throw new InvalidOperationException(
                "HwndSource self-test failed: Application.Current is null. " +
                "SnoopAgent must be started inside a running WPF Application.");
        }

        // Application.MainWindow and Application.Activated must be accessed on the
        // WPF dispatcher thread.  Marshal via Dispatcher.Invoke so this method is safe
        // to call from any thread (including the thread-pool task in RunServerAsync).
        var dispatcher = app.Dispatcher;

        Exception? dispatcherError = null;
        bool hasWindow = false;

        dispatcher.Invoke(
            () =>
            {
                try
                {
                    hasWindow = app.MainWindow is not null;
                }
                catch (Exception ex)
                {
                    dispatcherError = ex;
                }
            },
            DispatcherPriority.Normal);

        if (dispatcherError is not null)
        {
            throw new InvalidOperationException(
                "HwndSource self-test failed while checking MainWindow on the dispatcher thread. " +
                "See inner exception for details.", dispatcherError);
        }

        if (hasWindow)
        {
            // Happy path: window already exists — verify HwndSource resolution on the STA thread.
            dispatcher.Invoke(
                () =>
                {
                    try
                    {
                        VerifyHwndSource(app.MainWindow!);
                    }
                    catch (Exception ex)
                    {
                        dispatcherError = ex;
                    }
                },
                DispatcherPriority.Normal);

            if (dispatcherError is not null)
            {
                throw new InvalidOperationException(
                    "HwndSource self-test failed during HwndSource.FromVisual call. " +
                    "See inner exception for details.", dispatcherError);
            }

            Trace.TraceInformation("SnoopWPF.Agent SelfTest: HwndSourcePresent passed.");
            return;
        }

        // Headless path: no window yet.  Register a one-shot Activated handler on the dispatcher
        // and block the calling thread until either the window appears or the timeout elapses.
        Trace.TraceInformation(
            "SnoopWPF.Agent SelfTest: HwndSourcePresent — no main window yet; " +
            "waiting up to {0} ms for Application.Activated.", HeadlessWindowTimeoutMs);

        using var readyEvent = new ManualResetEventSlim(initialState: false);
        Exception? activationError = null;

        // Handler is attached and invoked on the STA dispatcher thread so all WPF accesses are safe.
        EventHandler activatedHandler = null!;
        activatedHandler = (_, _) =>
        {
            app.Activated -= activatedHandler;
            try
            {
                var window = app.MainWindow;
                if (window is not null)
                {
                    VerifyHwndSource(window);
                }
                else
                {
                    activationError = new InvalidOperationException(
                        "HwndSource self-test failed: Application.Activated fired but " +
                        "Application.Current.MainWindow is still null.");
                }
            }
            catch (Exception ex)
            {
                activationError = ex;
            }
            finally
            {
                readyEvent.Set();
            }
        };

        dispatcher.Invoke(() => app.Activated += activatedHandler, DispatcherPriority.Normal);

        bool signalled = readyEvent.Wait(HeadlessWindowTimeoutMs);

        if (!signalled)
        {
            // Clean up the dangling handler so it does not fire later.
            dispatcher.Invoke(() => app.Activated -= activatedHandler, DispatcherPriority.Normal);

            throw new InvalidOperationException(
                $"HwndSource self-test failed: no window appeared within {HeadlessWindowTimeoutMs} ms. " +
                "Ensure Application.Current.MainWindow is set before or shortly after " +
                "SnoopAgent.StartCoLocated() is called.");
        }

        if (activationError is not null)
        {
            throw activationError;
        }

        Trace.TraceInformation("SnoopWPF.Agent SelfTest: HwndSourcePresent passed (headless path).");
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>Timeout (ms) for the headless-window wait in <see cref="HwndSourcePresent"/>.</summary>
    internal const int HeadlessWindowTimeoutMs = 10_000;

    private static void VerifyHwndSource(Window window)
    {
        // HwndSource.FromVisual returns null when the visual has not been presented yet
        // (before the window handle is created).  We attempt the call; a non-throwing
        // return of null is acceptable at this point because the handle may materialise
        // once the Dispatcher pumps.  What we are actually guarding against is a
        // PlatformNotSupportedException or similar hard failure on headless machines.
        _ = HwndSource.FromVisual(window);
    }

#if NET8_0_OR_GREATER
    // [UnsafeAccessor] static method: reads the private backing field _value on SelfTestTarget.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_value")]
    private static extern ref int GetSelfTestValue(ref SelfTestTarget target);
#endif
}

#if NET8_0_OR_GREATER
/// <summary>
/// Minimal struct used as the [UnsafeAccessor] smoke-test target.
/// Kept internal and in the same assembly to avoid cross-assembly accessor concerns.
/// </summary>
internal struct SelfTestTarget
{
    // Field name must match the [UnsafeAccessor(Name = "_value")] binding exactly.
    // ReSharper disable once InconsistentNaming
#pragma warning disable SA1309 // Field '_value' should not begin with an underscore
    private int _value;
#pragma warning restore SA1309

    internal SelfTestTarget(int value) => this._value = value;
}
#endif
