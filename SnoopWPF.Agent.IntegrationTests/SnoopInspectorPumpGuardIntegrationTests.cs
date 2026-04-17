namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX5-pumpguard-restore: Integration-level regression for the FX-M1 pump-in-progress
/// guard introduced in <c>SnoopInspector.PumpUntilIdleAsync</c>.
///
/// FX-M1 fix: <c>PumpUntilIdleAsync</c> uses an instance-level <c>Interlocked</c> counter
/// rather than a <c>[ThreadStatic]</c> so that concurrent calls are correctly serialised
/// (or rejected with <see cref="SnoopErrorCode.DispatcherBusy"/>) even when continuations
/// migrate across thread-pool threads.
///
/// The original unit tests in <c>SnoopInspectorPumpGuardTests</c> were marked
/// <c>[Ignore]</c> because the isolated <c>Dispatcher.Run()</c> loop they create cannot
/// idle inside the test-harness thread: there is no real WPF message queue to drain, so
/// <c>ContextIdle</c> never fires, and the pump always times out instead of completing.
/// This integration test avoids that by running against the shared <see cref="TestWpfApp"/>
/// which has a live, pumping Dispatcher, exactly as
/// <see cref="SnoopInspectorDisposeRaceIntegrationTests"/> does for the dispose-race fix.
///
/// Category "RequiresWpf" can be excluded in headless CI with:
///   dotnet test --filter "Category!=RequiresWpf"
/// </summary>
[TestFixture]
[Category("RequiresWpf")]
[NonParallelizable]
public sealed class SnoopInspectorPumpGuardIntegrationTests : WpfIntegrationTestBase
{
    /// <summary>
    /// Two concurrent <c>PumpUntilIdleAsync</c> calls on the SAME inspector instance:
    /// one wins the Interlocked guard, the other must be rejected with
    /// <see cref="SnoopErrorCode.DispatcherBusy"/>.
    ///
    /// This is the integration-test replacement for
    /// <c>SnoopInspectorPumpGuardTests.TwoConcurrentPumps_OneWinsOtherGetsDispatcherBusy</c>
    /// which was ignored in the unit-test project because the isolated Dispatcher cannot idle.
    /// </summary>
    [Test]
    [CancelAfter(20_000)]
    public async Task TwoConcurrentPumps_OneWinsOtherGetsDispatcherBusy(CancellationToken cancellationToken)
    {
        // Create a dedicated inspector that we will use for the concurrent pump test.
        // Use a short timeout so the winning pump exits quickly, freeing the test.
        using var inspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 5_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        // Allow the dispatcher to settle before starting the race.
        await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);

        var task1 = Task.Run(
            () => inspector.PumpUntilIdleAsync(2000, Array.Empty<string>(), CancellationToken.None),
            CancellationToken.None);

        // Small delay so task1 acquires the Interlocked guard before task2 attempts to enter.
        await Task.Delay(15, CancellationToken.None).ConfigureAwait(false);

        var task2 = Task.Run(
            () => inspector.PumpUntilIdleAsync(2000, Array.Empty<string>(), CancellationToken.None),
            CancellationToken.None);

        // Collect outcomes — do not propagate exceptions here.
        await Task.WhenAll(
            task1.ContinueWith(_ => { }, TaskContinuationOptions.None),
            task2.ContinueWith(_ => { }, TaskContinuationOptions.None)).ConfigureAwait(false);

        var tasks = new[] { task1, task2 };
        var faulted = tasks.Where(t => t.IsFaulted).ToList();
        var succeeded = tasks.Where(t => t.IsCompletedSuccessfully).ToList();

        // Exactly one must have thrown SnoopException with DispatcherBusy.
        Assert.That(
            faulted,
            Has.Count.EqualTo(1),
            "Exactly one of the two concurrent pumps must fail with DispatcherBusy. " +
            $"Faulted={faulted.Count}, Succeeded={succeeded.Count}.");

        var ex = faulted[0].Exception!.GetBaseException() as SnoopException;
        Assert.That(ex, Is.Not.Null, "The faulted task must throw SnoopException.");
        Assert.That(
            ex!.Code,
            Is.EqualTo(SnoopErrorCode.DispatcherBusy),
            "The rejected pump must surface SnoopErrorCode.DispatcherBusy.");

        // The other must have completed (idle reached or timed out — both acceptable).
        Assert.That(
            succeeded.Count + faulted.Count,
            Is.EqualTo(2),
            "Both tasks must have completed (one succeeded, one faulted).");
    }

    /// <summary>
    /// Two sequential <c>PumpUntilIdleAsync</c> calls must both succeed.
    /// The Interlocked guard must reset to 0 in the finally block so the second call
    /// is not spuriously rejected.
    ///
    /// This is the integration-test replacement for
    /// <c>SnoopInspectorPumpGuardTests.SequentialPumps_BothSucceed</c>
    /// which was ignored in the unit-test project because the isolated Dispatcher cannot idle.
    /// </summary>
    [Test]
    [CancelAfter(20_000)]
    public async Task SequentialPumps_BothSucceed(CancellationToken cancellationToken)
    {
        using var inspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 5_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        // Allow the dispatcher to settle.
        await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);

        // First pump — must not throw DispatcherBusy.
        Exception? firstEx = null;
        try
        {
            await inspector.PumpUntilIdleAsync(3000, Array.Empty<string>(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
        {
            firstEx = ex;
        }

        Assert.That(firstEx, Is.Null,
            "First sequential pump must not be rejected with DispatcherBusy. " +
            "If this fails, the guard was not reset after the first call.");

        // Second pump — the guard must have been reset to 0 in the finally block.
        Exception? secondEx = null;
        try
        {
            await inspector.PumpUntilIdleAsync(3000, Array.Empty<string>(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
        {
            secondEx = ex;
        }

        Assert.That(secondEx, Is.Null,
            "Second sequential pump must not be rejected with DispatcherBusy. " +
            "The guard must reset to 0 in the finally block of the first pump.");
    }
}
