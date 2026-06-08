namespace SnoopWPF.Agent.Tests.Diagnostics;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Diagnostics;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Verifies <see cref="SnoopInspector.WarnIfModallyBlocked"/> emits a <c>MODAL_BLOCKED</c>
/// warning when an interaction targets an element whose owning window is disabled — the exact
/// state <see cref="Window.ShowDialog"/> imposes on every other top-level window via the Win32
/// <c>EnableWindow(hwnd, false)</c> call. Disabling the HWND directly reproduces that condition
/// deterministically without spinning a real nested modal message loop.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ModalGuardTests
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
            Name = "ModalGuardTests-Dispatcher",
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
    public void DisabledOwnerWindow_EmitsModalBlockedWarning()
    {
        var warnings = this.dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = "Recording",
                Width = 100,
                Height = 100,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
            };
            var button = new Button { Name = "captureButton" };
            window.Content = button;
            window.Show();

            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                Assert.That(hwnd, Is.Not.EqualTo(IntPtr.Zero), "Shown window must have an HWND.");

                // Reproduce the owner-disabled state ShowDialog imposes on background windows.
                SafeNativeMethods.EnableWindow(hwnd, false);

                using (SnoopAgentContext.BeginScope())
                {
                    SnoopInspector.WarnIfModallyBlocked(button);
                    return SnoopAgentContext.DrainWarnings();
                }
            }
            finally
            {
                window.Close();
            }
        });

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0].Code, Is.EqualTo("MODAL_BLOCKED"));
        Assert.That(warnings[0].Message, Does.Contain("disabled"));
    }

    [Test]
    public void EnabledWindow_EmitsNoWarning()
    {
        var warnings = this.dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = "Main",
                Width = 100,
                Height = 100,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
            };
            var button = new Button { Name = "okButton" };
            window.Content = button;
            window.Show();

            try
            {
                using (SnoopAgentContext.BeginScope())
                {
                    SnoopInspector.WarnIfModallyBlocked(button);
                    return SnoopAgentContext.DrainWarnings();
                }
            }
            finally
            {
                window.Close();
            }
        });

        Assert.That(warnings, Is.Empty, "An enabled window is not modally blocked.");
    }

    private static class SafeNativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);
    }
}
