// SnoopWPF.Agent.Tests/SnoopAgentStartupTests.cs
// FX6-D1: acceptance tests for SnoopAgent startup failure surface
// (IStartupFailureSink, IsStarted, StartupException).

namespace SnoopWPF.Agent.Tests;

using System;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Engine.Audit;
using SnoopWPF.Agent.Server;

/// <summary>
/// Validates FX6-D1: startup failure surface on <see cref="SnoopAgentHandle"/>.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SnoopAgentStartupTests
{
    // -------------------------------------------------------------------------
    // Helper: create a bare handle without going through SnoopAgent.StartCoLocated.
    // -------------------------------------------------------------------------

    private static SnoopAgentHandle MakeHandle()
    {
        var cts = new CancellationTokenSource();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var inspector = new SnoopInspector(dispatcher);
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions());
        return new SnoopAgentHandle(cts, inspector, policy);
    }

    // -------------------------------------------------------------------------
    // IsStarted defaults to false
    // -------------------------------------------------------------------------

    [Test]
    public void Handle_IsStarted_DefaultsFalse()
    {
        using var handle = MakeHandle();
        Assert.That(handle.IsStarted, Is.False, "IsStarted must default to false before startup completes.");
    }

    // -------------------------------------------------------------------------
    // StartupException defaults to null
    // -------------------------------------------------------------------------

    [Test]
    public void Handle_StartupException_DefaultsNull()
    {
        using var handle = MakeHandle();
        Assert.That(handle.StartupException, Is.Null, "StartupException must default to null.");
    }

    // -------------------------------------------------------------------------
    // IStartupFailureSink.OnStartupFailed populates StartupException
    // -------------------------------------------------------------------------

    [Test]
    public void OnStartupFailed_SetsStartupException()
    {
        using var handle = MakeHandle();
        var sink = (IStartupFailureSink)handle;
        var ex = new InvalidOperationException("test startup failure");

        sink.OnStartupFailed(ex);

        Assert.That(handle.StartupException, Is.SameAs(ex),
            "StartupException must be the exception passed to OnStartupFailed.");
    }

    // -------------------------------------------------------------------------
    // IStartupFailureSink.OnStartupFailed is idempotent (only first call wins)
    // -------------------------------------------------------------------------

    [Test]
    public void OnStartupFailed_Idempotent_OnlyFirstCallWins()
    {
        using var handle = MakeHandle();
        var sink = (IStartupFailureSink)handle;
        var ex1 = new InvalidOperationException("first");
        var ex2 = new InvalidOperationException("second");

        sink.OnStartupFailed(ex1);
        sink.OnStartupFailed(ex2);

        Assert.That(handle.StartupException, Is.SameAs(ex1),
            "Only the first OnStartupFailed call must be recorded.");
    }

    // -------------------------------------------------------------------------
    // IsStarted can be set to true (simulates successful startup)
    // -------------------------------------------------------------------------

    [Test]
    public void Handle_IsStarted_CanBeSetTrue()
    {
        using var handle = MakeHandle();
        handle.IsStarted = true;

        Assert.That(handle.IsStarted, Is.True);
    }

    // -------------------------------------------------------------------------
    // InvalidOptions_ThrowsAtStartAsync acceptance (via sink surface)
    // Simulates bad-options path: OnStartupFailed is called → StartupException != null.
    // -------------------------------------------------------------------------

    [Test]
    public void InvalidOptions_ThrowsAtStartAsync_StartupExceptionIsSet()
    {
        using var handle = MakeHandle();
        var sink = (IStartupFailureSink)handle;

        // Simulate what RunServerAsync does when SelfTest or transport setup fails.
        var startupError = new InvalidOperationException("HwndSource not present — startup self-test failed.");
        sink.OnStartupFailed(startupError);

        Assert.That(handle.StartupException, Is.Not.Null,
            "StartupException must be non-null after a simulated startup failure.");
        Assert.That(handle.StartupException!.Message, Does.Contain("HwndSource"));
        Assert.That(handle.IsStarted, Is.False,
            "IsStarted must remain false when startup failed before RunAsync.");
    }
}
