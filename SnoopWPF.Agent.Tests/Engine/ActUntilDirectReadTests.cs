// SnoopWPF.Agent.Tests/Engine/ActUntilDirectReadTests.cs
// Unit coverage for the act_until direct-DP-read fast path
// (replaces per-poll PropertyInformation.GetProperties).

namespace SnoopWPF.Agent.Tests.Engine;

using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Verifies <see cref="SnoopInspector.TryReadStringValue"/>: the cached, allocation-free
/// property reader that powers the act_until poll loop. Asserts:
/// (1) standard DP read returns the live value as string,
/// (2) CLR-only property fallback works (PasswordBox.Password is the canonical case),
/// (3) unknown property names return false without throwing,
/// (4) repeated reads on the same (Type, name) pair are noticeably faster after caching.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ActUntilDirectReadTests
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
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
            Name = "ActUntilDirectReadTests-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(System.TimeSpan.FromSeconds(5));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(System.TimeSpan.FromSeconds(3));
    }

    [Test]
    public void StandardDp_IsEnabled_ReadsTrue()
    {
        this.dispatcher.Invoke(() =>
        {
            var btn = new Button { Content = "x", IsEnabled = true };
            var ok = SnoopInspector.TryReadStringValue(btn, "IsEnabled", out var value);
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo("True"));
        });
    }

    [Test]
    public void StandardDp_Visibility_ReadsEnumName()
    {
        this.dispatcher.Invoke(() =>
        {
            var fe = new FrameworkElement { Visibility = Visibility.Collapsed };
            var ok = SnoopInspector.TryReadStringValue(fe, "Visibility", out var value);
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo("Collapsed"));
        });
    }

    [Test]
    public void StandardDp_PropertyName_IsCaseInsensitive()
    {
        this.dispatcher.Invoke(() =>
        {
            var btn = new Button();
            Assert.That(SnoopInspector.TryReadStringValue(btn, "isEnabled", out var lower), Is.True);
            Assert.That(SnoopInspector.TryReadStringValue(btn, "ISENABLED", out var upper), Is.True);
            Assert.That(lower, Is.EqualTo(upper));
        });
    }

    [Test]
    public void ClrFallback_PasswordBox_Password()
    {
        // PasswordBox.Password is intentionally a CLR-only property (not a DP) for
        // security reasons. Verifies the CLR-property fallback path in PropertyAccessor.
        this.dispatcher.Invoke(() =>
        {
            var pw = new PasswordBox();
            pw.Password = "hunter2";
            var ok = SnoopInspector.TryReadStringValue(pw, "Password", out var value);
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void UnknownProperty_ReturnsFalse_DoesNotThrow()
    {
        this.dispatcher.Invoke(() =>
        {
            var btn = new Button();
            var ok = SnoopInspector.TryReadStringValue(btn, "ThisPropertyDoesNotExistAnywhere", out var value);
            Assert.That(ok, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void RepeatedRead_IsCached_AndFast()
    {
        // Smoke-level perf assert: the first read does the descriptor walk; subsequent
        // reads should be ~free (a dict lookup + GetValue). 1000 cached reads under 50ms
        // is conservative and ~50× faster than the old per-poll allocation storm.
        this.dispatcher.Invoke(() =>
        {
            var btn = new Button();

            // Prime the cache.
            SnoopInspector.TryReadStringValue(btn, "IsVisible", out _);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                SnoopInspector.TryReadStringValue(btn, "IsVisible", out _);
            }

            sw.Stop();
            Assert.That(
                sw.ElapsedMilliseconds,
                Is.LessThan(50),
                $"1000 cached reads took {sw.ElapsedMilliseconds}ms — cache may be missing.");
        });
    }
}
