namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX4-C1-test: Integration-level regression for the dispose-race fix introduced by FX2-C1.
///
/// FX2-C1 fix: <c>SnoopInspector.Dispose</c> wraps <c>concurrencySemaphore.Release()</c>
/// in a <c>catch (ObjectDisposedException)</c> so that releasing the semaphore slot after
/// Dispose has already disposed it does NOT surface as an unobserved task exception
/// (which is AppDomain-fatal on net462 and causes CI failures on all TFMs).
///
/// This test replaces the role of the ignored unit test
/// <c>SnoopInspectorDisposeRaceTests.ConcurrentCallersAndDispose_NoObjectDisposedException</c>
/// in <c>SnoopWPF.Agent.Tests</c>. The unit-test harness deadlocks under that concurrent-dispose
/// pattern because the isolated <c>Dispatcher.Run()</c> loop cannot pump messages during
/// dispose; that is a test-harness artifact, not a product bug. This integration test avoids
/// the deadlock by running against the shared real WPF <see cref="TestWpfApp"/> which already
/// has a live, pumping Dispatcher.
/// </summary>
/// <remarks>
/// Category "RequiresWpf" matches the convention in <see cref="WpfIntegrationTestBase"/> and
/// can be excluded in headless CI with: dotnet test --filter "Category!=RequiresWpf"
/// </remarks>
[TestFixture]
[Category("RequiresWpf")]
[NonParallelizable]
public sealed class SnoopInspectorDisposeRaceIntegrationTests : WpfIntegrationTestBase
{
    /// <summary>
    /// Spawns 30 concurrent <c>GetSessionInfoAsync</c> callers, then disposes the inspector
    /// mid-flight. After all tasks settle:
    /// <list type="bullet">
    ///   <item>No caller task may be faulted with a BARE <see cref="ObjectDisposedException"/>
    ///   at the top level — that would mean FX2-C1 is broken.</item>
    ///   <item>Callers may complete successfully, or fault with
    ///   <see cref="OperationCanceledException"/> (dispose CTS cancellation path — happy path),
    ///   or fault with a <see cref="SnoopWPF.Agent.Contracts.SnoopException"/>
    ///   (e.g. DispatcherBusy, SessionNotFound — also acceptable).</item>
    ///   <item>At least one task must have faulted or cancelled (proves Dispose ran while
    ///   callers were in flight and the test is not trivially vacuous).</item>
    /// </list>
    /// </summary>
    [Test]
    [CancelAfter(20_000)]
    public async Task ConcurrentGetSessionInfoAndDispose_NoBareObjectDisposedException(
        CancellationToken cancellationToken)
    {
        // Create a dedicated inspector that we will dispose mid-flight.
        // Use a generous timeout so tasks do not time out before we can assert on them.
        using var inspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        const int callerCount = 30;
        var tasks = new Task[callerCount];

        // Fan-out: launch all callers before Dispose runs.
        // Using Task.Run so callers are on thread-pool threads (not the Dispatcher thread).
        for (int i = 0; i < callerCount; i++)
        {
            tasks[i] = Task.Run(
                () => inspector.GetSessionInfoAsync(CancellationToken.None),
                CancellationToken.None);
        }

        // Give callers a moment to reach the semaphore WaitAsync so at least some are
        // queued inside RunOnDispatcherAsync when Dispose fires.
        await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);

        // Dispose on the current (background) thread — this is the race scenario.
        inspector.Dispose();

        // Collect outcomes; we do NOT propagate exceptions here.
        var bareObjectDisposedExceptions = new List<ObjectDisposedException>();
        var cancelledCount = 0;
        var succeededCount = 0;
        var otherFaultCount = 0;

        foreach (var task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
                succeededCount++;
            }
            catch (OperationCanceledException)
            {
                // Expected happy path: dispose CTS cancelled the semaphore WaitAsync.
                cancelledCount++;
            }
            catch (ObjectDisposedException ode)
            {
                // This is the failure scenario FX2-C1 is supposed to prevent.
                bareObjectDisposedExceptions.Add(ode);
            }
            catch (Exception)
            {
                // SnoopException(SessionNotFound), SnoopException(DispatcherBusy), etc.
                // All acceptable: they are domain errors, not concurrency bugs.
                otherFaultCount++;
            }
        }

        // Primary assertion: no bare ObjectDisposedException must escape.
        Assert.That(
            bareObjectDisposedExceptions,
            Is.Empty,
            $"FX2-C1 regression: {bareObjectDisposedExceptions.Count} bare ObjectDisposedException(s) " +
            $"escaped from GetSessionInfoAsync during concurrent Dispose. " +
            $"Details: {string.Join("; ", bareObjectDisposedExceptions.Select(e => e.Message))}");

        // Sanity assertion: at least one task must NOT have succeeded cleanly, proving
        // that Dispose actually raced with in-flight callers (otherwise the test is vacuous).
        var disturbedCount = cancelledCount + otherFaultCount + bareObjectDisposedExceptions.Count;
        Assert.That(
            disturbedCount,
            Is.GreaterThan(0),
            $"Expected at least one caller to be disturbed by Dispose, but all {callerCount} " +
            $"completed successfully — Dispose may not have raced with any in-flight caller. " +
            $"Succeeded={succeededCount}, Cancelled={cancelledCount}, Other={otherFaultCount}.");
    }
}
