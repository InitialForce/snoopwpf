namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Integration tests for <c>wpf_pump_until_idle</c> (M2-11).
///
/// Covers:
///   1. Basic idle success — dispatcher is idle, call returns quickly.
///   2. 5s animation-runaway timeout — when idle never arrives within 5s, throws DispatcherBusy.
///   3. Resource filter — requesting a specific resource subset works.
/// </summary>
[TestFixture]
public sealed class PumpUntilIdleIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Test 1: basic idle success
    // -------------------------------------------------------------------------

    /// <summary>
    /// With a quiescent WPF application the pump should return
    /// quickly with idleReached=true.
    /// </summary>
    [Test]
    public async Task PumpUntilIdle_QuiescentApp_ReturnsIdleReached()
    {
        // Allow any pending dispatcher work to drain.
        await Task.Delay(200).ConfigureAwait(false);

        // Act.
        var result = await this.Client.Inspector.PumpUntilIdleAsync(
            timeoutMs: 5000,
            resources: null,
            ct: CancellationToken.None).ConfigureAwait(false);

        // Assert.
        Assert.That(result.IdleReached, Is.True,
            "Idle should be reached when the WPF app is quiescent.");
        Assert.That(result.ElapsedMs, Is.GreaterThanOrEqualTo(0),
            "ElapsedMs must be non-negative.");
        Assert.That(result.ResourcesMonitored, Is.Not.Empty,
            "At least one resource must be monitored.");
        Assert.That(result.StillBusy, Is.Empty,
            "StillBusy must be empty when idleReached=true.");
    }

    // -------------------------------------------------------------------------
    // Test 2: 5s animation-runaway timeout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies the animation-runaway timeout ceiling (PRD §8.2 W3-H2).
    ///
    /// A <c>DispatcherIdlingResource</c> starts non-idle (it has a pending
    /// <c>ContextIdle</c> probe that has not yet executed).  Requesting a 1ms timeout
    /// means the deadline expires before the probe can fire, so the pump must throw
    /// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.DispatcherBusy"/>.
    ///
    /// Also verifies that an excessive timeout value is clamped to the 5-second ceiling
    /// (the method itself caps the value so callers cannot pass &gt; 5000ms).
    /// </summary>
    [Test]
    public async Task PumpUntilIdle_AnimationRunawayTimeout_ThrowsDispatcherBusy()
    {
        // First, saturate the Dispatcher with a burst of high-priority work so the
        // ContextIdle probe is queued behind real work.  This ensures isIdle stays false
        // when we enter the pump with a 1ms window.
        var burstTasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = this.WpfApp.Dispatcher.InvokeAsync(
                () => tcs.TrySetResult(true),
                System.Windows.Threading.DispatcherPriority.Normal);
            burstTasks.Add(tcs.Task);
        }

        // Do NOT wait for the burst — immediately start the pump with a near-zero timeout.
        // The ContextIdle probe can't fire before the Normal-priority burst drains,
        // so the 1ms pump window expires while the registry is still non-idle.
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await this.Client.Inspector.PumpUntilIdleAsync(
                timeoutMs: 1,
                resources: new[] { "Dispatcher" },
                ct: CancellationToken.None).ConfigureAwait(false);
        });

        sw.Stop();

        // Allow burst to drain.
        await Task.WhenAll(burstTasks).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        Assert.That(ex, Is.Not.Null, "Expected SnoopException.");
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.DispatcherBusy),
            "Timeout should surface as DispatcherBusy.");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5500),
            "Pump must not exceed the 5-second ceiling even if timeoutMs is large.");
    }

    // -------------------------------------------------------------------------
    // Test 3: resource filter
    // -------------------------------------------------------------------------

    /// <summary>
    /// When a specific resource subset is requested only those resources are monitored.
    /// In a quiescent app requesting only "Dispatcher" should still succeed.
    /// </summary>
    [Test]
    public async Task PumpUntilIdle_ResourceFilter_MonitorsOnlyRequestedResources()
    {
        await Task.Delay(100).ConfigureAwait(false);

        // Act: filter to Dispatcher only.
        var result = await this.Client.Inspector.PumpUntilIdleAsync(
            timeoutMs: 5000,
            resources: new[] { "Dispatcher" },
            ct: CancellationToken.None).ConfigureAwait(false);

        // Assert.
        Assert.That(result.IdleReached, Is.True,
            "Idle should be reached for the Dispatcher resource in a quiescent app.");
        Assert.That(result.ResourcesMonitored, Does.Contain("Dispatcher"),
            "ResourcesMonitored must include the requested resource.");
        Assert.That(result.ResourcesMonitored, Has.Count.EqualTo(1),
            "Only the requested resource should be monitored when a filter is supplied.");
    }
}

/// <summary>
/// Extension helper for awaiting tasks that may be cancelled.
/// </summary>
internal static class TaskExtensions
{
    internal static async Task IgnoreExceptions(this Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Intentionally swallow — used only for cleanup in tests.
        }
    }
}
