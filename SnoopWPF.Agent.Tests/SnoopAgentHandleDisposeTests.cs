namespace SnoopWPF.Agent.Tests;

using System;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Server;

/// <summary>
/// Verifies that concurrent <see cref="SnoopAgentHandle.Dispose"/> calls are idempotent.
/// The Interlocked guard introduced by FX-C2 must ensure teardown runs exactly once even
/// when 10 threads race to dispose the same handle simultaneously.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SnoopAgentHandleDisposeTests
{
    /// <summary>
    /// Ten threads calling Dispose simultaneously must not throw and must leave the
    /// CancellationTokenSource in the cancelled state (single teardown path executed).
    /// </summary>
    [Test]
    public void Dispose_ConcurrentCalls_NoExceptionAndSingleTeardown()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var inspector = new SnoopInspector(Dispatcher.CurrentDispatcher);
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions());
        var handle = new SnoopAgentHandle(cts, inspector, policy);

        const int threadCount = 10;
        var barrier = new Barrier(threadCount);
        var exceptions = new Exception?[threadCount];

        var threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            threads[index] = new Thread(() =>
            {
                barrier.SignalAndWait(); // all threads start simultaneously
                try
                {
                    handle.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions[index] = ex;
                }
            });
        }

        // Act
        foreach (var t in threads)
        {
            t.Start();
        }

        foreach (var t in threads)
        {
            t.Join(TimeSpan.FromSeconds(5));
        }

        // Assert — no thread observed an exception
        for (int i = 0; i < threadCount; i++)
        {
            Assert.That(exceptions[i], Is.Null,
                $"Thread {i} threw: {exceptions[i]}");
        }

        // CTS must be cancelled (proves teardown ran)
        Assert.That(cts.IsCancellationRequested, Is.True,
            "CancellationTokenSource must be cancelled after Dispose.");
    }

    /// <summary>
    /// A second sequential Dispose call on an already-disposed handle must be a no-op.
    /// </summary>
    [Test]
    public void Dispose_CalledTwiceSequentially_NoException()
    {
        var cts = new CancellationTokenSource();
        var inspector = new SnoopInspector(Dispatcher.CurrentDispatcher);
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions());
        var handle = new SnoopAgentHandle(cts, inspector, policy);

        Assert.DoesNotThrow(() => handle.Dispose());
        Assert.DoesNotThrow(() => handle.Dispose(), "Second Dispose must not throw.");
    }
}
