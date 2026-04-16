namespace SnoopWPF.Agent.IntegrationTests;

using System.Windows;
using NUnit.Framework;
using SnoopWPF.Agent.Server;

/// <summary>
/// Integration tests for <see cref="SelfTest"/> — boot-sequence step 5 (PRD §4.2).
/// These run inside a real WPF application so all assertions exercise live WPF objects.
/// </summary>
[TestFixture]
public sealed class SelfTestTests : WpfIntegrationTestBase
{
    /// <summary>
    /// <see cref="SelfTest.UnsafeAccessorBindings"/> must not throw in a normal WPF process.
    /// On net8.0+ this exercises the [UnsafeAccessor] path; on earlier TFMs it is a no-op.
    /// </summary>
    [Test]
    public void UnsafeAccessorBindings_DoesNotThrow()
    {
        Assert.That(() => SelfTest.UnsafeAccessorBindings(), Throws.Nothing,
            "UnsafeAccessorBindings() must not throw when running inside a WPF process.");
    }

    /// <summary>
    /// <see cref="SelfTest.HwndSourcePresent"/> must not throw when
    /// <see cref="Application.Current.MainWindow"/> is already set (normal flow).
    /// The method marshals WPF access internally via the dispatcher, so it is safe to
    /// call from any thread.
    /// </summary>
    [Test]
    public void HwndSourcePresent_WithMainWindow_DoesNotThrow()
    {
        // Verify preconditions via the STA dispatcher (required for WPF property access).
        bool hasWindow = false;
        this.WpfApp.Dispatcher.Invoke(() => hasWindow = Application.Current?.MainWindow is not null);
        Assert.That(hasWindow, Is.True,
            "Application.Current.MainWindow must be set by the test fixture before this test runs.");

        // SelfTest.HwndSourcePresent() internally uses Dispatcher.Invoke, so it is safe
        // to call directly from the NUnit test thread (not the STA thread).
        Assert.That(() => SelfTest.HwndSourcePresent(), Throws.Nothing,
            "HwndSourcePresent() must not throw when a main window is present.");
    }

    /// <summary>
    /// <see cref="SelfTest.HwndSourcePresent"/> must be idempotent — a second consecutive
    /// call must also succeed without throwing.
    /// </summary>
    [Test]
    public void HwndSourcePresent_CalledTwice_BothSucceed()
    {
        Assert.That(() => SelfTest.HwndSourcePresent(), Throws.Nothing,
            "First call to HwndSourcePresent() must not throw.");

        Assert.That(() => SelfTest.HwndSourcePresent(), Throws.Nothing,
            "Second call to HwndSourcePresent() must also not throw (idempotent).");
    }

    /// <summary>
    /// Verifies the headless-window timeout constant is a positive integer matching the
    /// PRD §4.2 specification of 10 000 ms.
    /// </summary>
    [Test]
    public void HwndSourcePresent_HeadlessTimeout_IsPositive()
    {
        Assert.That(SelfTest.HeadlessWindowTimeoutMs, Is.GreaterThan(0),
            "HeadlessWindowTimeoutMs must be positive.");
        Assert.That(SelfTest.HeadlessWindowTimeoutMs, Is.EqualTo(10_000),
            "HeadlessWindowTimeoutMs must be 10 000 ms as specified in PRD §4.2.");
    }
}
