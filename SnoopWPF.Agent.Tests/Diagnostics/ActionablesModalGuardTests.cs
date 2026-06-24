namespace SnoopWPF.Agent.Tests.Diagnostics;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Diagnostics;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Verifies the actionables discovery path (<see cref="SnoopInspector.GetActionablesAsync"/>,
/// behind <c>wpf_get_actionables</c>) emits a <c>MODAL_BLOCKED</c> warning when its resolved
/// root lives in a window disabled by an open modal dialog — the state
/// <see cref="Window.ShowDialog"/> imposes on background windows via the Win32
/// <c>EnableWindow(hwnd, false)</c> call. Without this, a modal makes the walk return an empty
/// items list indistinguishable from a genuinely empty screen (DESKTOP-11838). Disabling the
/// HWND directly reproduces the condition deterministically without a nested modal loop.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ActionablesModalGuardTests
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;

    [OneTimeSetUp]
    public void SetUpDispatcher()
    {
        var ready = new ManualResetEventSlim(false);
        this.dispatcherThread = new Thread(() =>
        {
            this.dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "ActionablesModalGuardTests-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    [OneTimeTearDown]
    public void TearDownDispatcher()
    {
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(TimeSpan.FromSeconds(3));
    }

    [Test]
    public void GetActionables_DisabledOwnerWindow_EmitsModalBlockedWarning()
    {
        var (window, inspector) = this.CreateWindowAndInspector();

        try
        {
            this.dispatcher.Invoke(() =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                Assert.That(hwnd, Is.Not.EqualTo(IntPtr.Zero), "Shown window must have an HWND.");

                // Reproduce the owner-disabled state ShowDialog imposes on background windows.
                SafeNativeMethods.EnableWindow(hwnd, false);
            });

            // The scope must be opened on the calling thread before invoking the inspector so the
            // warning list reference flows into the dispatcher delegate (mirrors production, where
            // the pipe server opens the scope around the dispatched handler).
            using (SnoopAgentContext.BeginScope())
            {
                inspector.GetActionablesAsync(rootNodeId: null, maxResults: 50, default)
                    .GetAwaiter().GetResult();

                var warnings = SnoopAgentContext.DrainWarnings();

                Assert.That(warnings, Has.Count.EqualTo(1));
                Assert.That(warnings[0].Code, Is.EqualTo("MODAL_BLOCKED"));
                Assert.That(warnings[0].Message, Does.Contain("disabled"));
            }
        }
        finally
        {
            inspector.Dispose();
            this.dispatcher.Invoke(window.Close);
        }
    }

    [Test]
    public void GetActionables_EnabledWindow_EmitsNoWarning()
    {
        var (window, inspector) = this.CreateWindowAndInspector();

        try
        {
            using (SnoopAgentContext.BeginScope())
            {
                inspector.GetActionablesAsync(rootNodeId: null, maxResults: 50, default)
                    .GetAwaiter().GetResult();

                var warnings = SnoopAgentContext.DrainWarnings();

                Assert.That(warnings, Is.Empty, "An enabled window is not modally blocked.");
            }
        }
        finally
        {
            inspector.Dispose();
            this.dispatcher.Invoke(window.Close);
        }
    }

    private (Window Window, SnoopInspector Inspector) CreateWindowAndInspector()
    {
        var window = this.dispatcher.Invoke(() =>
        {
            var w = new Window
            {
                Title = "Recording",
                Width = 100,
                Height = 100,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
                Content = new Button { Name = "captureButton", Content = "Capture" },
            };
            w.Show();
            return w;
        });

        var inspector = new SnoopInspector(
            dispatcher: this.dispatcher,
            rootTarget: window,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        return (window, inspector);
    }

    private static class SafeNativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);
    }
}
