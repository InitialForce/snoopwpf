// SnoopWPF.Agent.Tests/PollChangesPerfTests.cs
// FX6-A6: acceptance tests — PollChangesAsync version-check fast path.

namespace SnoopWPF.Agent.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX6-A6 acceptance tests: verifies that the PollChangesAsync fast path (version
/// unchanged) does not consume significant Dispatcher time when the tree is stable.
/// </summary>
/// <remarks>
/// Acceptance criterion: when the visual tree is static (no nodes added/removed),
/// the p95 PollChangesAsync wall-clock latency must be &lt; 5 ms — meaning the
/// Dispatcher is occupied for well under 5% of a 100 ms polling interval.
/// Runs on a dedicated STA Dispatcher thread — no full Application required.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class PollChangesPerfTests : IDisposable
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;
    private Button button = null!;
    private SnoopInspector inspector = null!;

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
            Name = "PollChangesPerfTests-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        this.dispatcher.Invoke(() =>
        {
            this.button = new Button
            {
                Content = "PollPerfTest",
                Width = 100,
                Height = 30,
            };
        });

        this.inspector = new SnoopInspector(
            dispatcher: this.dispatcher,
            rootTarget: this.button,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        this.inspector?.Dispose();
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(TimeSpan.FromSeconds(3));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.inspector?.Dispose();
    }

    // -------------------------------------------------------------------------
    // FX6-A6: PollChangesAsync fast path p95 < 5ms on stable tree
    // -------------------------------------------------------------------------

    [Test]
    public void PollChangesAsync_StableTree_P95Under5Ms()
    {
        // Warm up: do an initial full-tree poll (registers nodes, primes lastPollLiveIds).
        var firstResult = this.inspector.PollChangesAsync(sinceVersion: -1, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        var baseline = firstResult.TreeVersion;

        // Second poll at current version — primes the fast-path state.
        this.inspector.PollChangesAsync(sinceVersion: baseline, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        // Measure 40 steady-state polls (version unchanged, tree is stable).
        const int iterations = 40;
        var durations = new List<long>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            this.inspector.PollChangesAsync(sinceVersion: baseline, rootLocator: null, ct: default)
                .GetAwaiter().GetResult();
            sw.Stop();
            durations.Add(sw.ElapsedMilliseconds);
        }

        durations.Sort();
        var p95Index = (int)Math.Ceiling(iterations * 0.95) - 1;
        var p95Ms = durations[Math.Min(p95Index, durations.Count - 1)];

        Assert.That(
            p95Ms,
            Is.LessThan(5),
            $"PollChangesAsync (stable tree, fast path) p95 latency is {p95Ms} ms — must be < 5 ms (FX6-A6). " +
            $"All durations (ms): [{string.Join(", ", durations)}]");
    }

    // -------------------------------------------------------------------------
    // FX6-A6: fast path returns empty Changes on stable tree
    // -------------------------------------------------------------------------

    [Test]
    public void PollChangesAsync_StableTree_ReturnsNoChanges()
    {
        var firstResult = this.inspector.PollChangesAsync(sinceVersion: -1, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        var baseline = firstResult.TreeVersion;

        // Prime fast path.
        this.inspector.PollChangesAsync(sinceVersion: baseline, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        // Steady-state poll: no changes expected.
        var result = this.inspector.PollChangesAsync(sinceVersion: baseline, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.Changes, Is.Empty,
            "PollChangesAsync on a stable tree must return an empty Changes list.");
        Assert.That(result.ChangeCount, Is.EqualTo(0),
            "PollChangesAsync ChangeCount must be 0 on a stable tree.");
    }

    // -------------------------------------------------------------------------
    // FX6-A6: first full-tree poll populates TreeVersion
    // -------------------------------------------------------------------------

    [Test]
    public void PollChangesAsync_FirstPoll_ReturnsTreeVersion()
    {
        var result = this.inspector.PollChangesAsync(sinceVersion: -1, rootLocator: null, ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.TreeVersion, Is.GreaterThanOrEqualTo(0),
            "PollChangesAsync must return a non-negative TreeVersion.");
    }
}
