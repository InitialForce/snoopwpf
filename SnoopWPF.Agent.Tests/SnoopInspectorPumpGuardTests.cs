namespace SnoopWPF.Agent.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX-M1: Verifies that the pump-in-progress guard uses an instance-level Interlocked
/// counter rather than a [ThreadStatic], so concurrent PumpUntilIdleAsync calls are
/// correctly rejected even when continuations migrate across thread-pool threads.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class SnoopInspectorPumpGuardTests
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;

    [SetUp]
    public void SetUp()
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
            Name = "PumpGuardTest-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    [TearDown]
    public void TearDown()
    {
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Two concurrent PumpUntilIdleAsync calls: one wins, the other gets DispatcherBusy
    /// (SnoopException with SnoopErrorCode.DispatcherBusy). No spurious failures.
    /// </summary>
    [Test]
    [CancelAfter(10_000)]
    public async Task TwoConcurrentPumps_OneWinsOtherGetsDispatcherBusy()
    {
        // Use a very short timeout so the winning pump exits quickly.
        var inspector = new SnoopInspector(this.dispatcher, options: new SnoopInspectorOptions
        {
            TimeoutMs = 2000,
        });

        using (inspector)
        {
            var task1 = Task.Run(() => inspector.PumpUntilIdleAsync(200, null, CancellationToken.None));
            // Small delay to ensure task1 acquires the guard before task2 starts.
            await Task.Delay(10);
            var task2 = Task.Run(() => inspector.PumpUntilIdleAsync(200, null, CancellationToken.None));

            // Both tasks must complete (one successfully, one with SnoopException).
            await Task.WhenAll(
                task1.ContinueWith(t => { /* swallow */ }, TaskContinuationOptions.None),
                task2.ContinueWith(t => { /* swallow */ }, TaskContinuationOptions.None));

            var faulted = new[] { task1, task2 }.Where(t => t.IsFaulted).ToList();
            var succeeded = new[] { task1, task2 }.Where(t => t.IsCompletedSuccessfully).ToList();

            // Exactly one must have thrown SnoopException with DispatcherBusy.
            Assert.That(faulted, Has.Count.EqualTo(1), "Exactly one pump should fail with DispatcherBusy");
            var ex = faulted[0].Exception!.GetBaseException() as SnoopException;
            Assert.That(ex, Is.Not.Null, "Faulted task should throw SnoopException");
            Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.DispatcherBusy));

            // The other must have returned a result (idle or timed out — both are valid).
            Assert.That(succeeded.Count + faulted.Count, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Sequential pumps must both succeed — the guard resets to 0 in the finally block.
    /// </summary>
    [Test]
    [CancelAfter(10_000)]
    public async Task SequentialPumps_BothSucceed()
    {
        var inspector = new SnoopInspector(this.dispatcher, options: new SnoopInspectorOptions
        {
            TimeoutMs = 2000,
        });

        using (inspector)
        {
            // First pump — may return idle or timeout, but must not throw DispatcherBusy.
            Exception? firstEx = null;
            try
            {
                await inspector.PumpUntilIdleAsync(100, null, CancellationToken.None);
            }
            catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
            {
                firstEx = ex;
            }

            Assert.That(firstEx, Is.Null, "First pump must not get DispatcherBusy");

            // Second pump — guard must have been reset.
            Exception? secondEx = null;
            try
            {
                await inspector.PumpUntilIdleAsync(100, null, CancellationToken.None);
            }
            catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
            {
                secondEx = ex;
            }

            Assert.That(secondEx, Is.Null, "Second sequential pump must not get DispatcherBusy");
        }
    }
}
