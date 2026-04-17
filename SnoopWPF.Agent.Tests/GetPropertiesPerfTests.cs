// SnoopWPF.Agent.Tests/GetPropertiesPerfTests.cs
// FX6-A5: acceptance tests — GetPropertiesAsync allocation storm fix.

namespace SnoopWPF.Agent.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX6-A5 acceptance tests: verifies that GetPropertiesAsync does not stall the
/// Dispatcher for > 20 ms p95 on a typical WPF element (Button has ~300 DPs).
/// </summary>
/// <remarks>
/// Runs on a dedicated STA Dispatcher thread — no full Application required.
/// The performance acceptance criterion is: p95 latency &lt; 20 ms for a Button
/// with <c>includeDefaults=false</c> (the default / common case).
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class GetPropertiesPerfTests : IDisposable
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
            Name = "GetPropertiesPerfTests-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        // Create a Button on the STA dispatcher — it exposes ~200-300 DPs.
        this.dispatcher.Invoke(() =>
        {
            this.button = new Button
            {
                Content = "PerfTest",
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
    // FX6-A5: GetPropertiesAsync p95 < 20ms with includeDefaults=false
    // -------------------------------------------------------------------------

    [Test]
    public void GetPropertiesAsync_IncludeDefaultsFalse_P95Under20Ms()
    {
        // Warm up (fill JIT, populate cursor snapshot).
        var rootId = this.GetRootNodeId();
        this.inspector.GetPropertiesAsync(rootId, null, null, includeDefaults: false, null, 50, default)
            .GetAwaiter().GetResult();

        // Measure 30 iterations.
        const int iterations = 30;
        var durations = new List<long>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            this.inspector.GetPropertiesAsync(rootId, null, null, includeDefaults: false, null, 50, default)
                .GetAwaiter().GetResult();
            sw.Stop();
            durations.Add(sw.ElapsedMilliseconds);
        }

        durations.Sort();
        var p95Index = (int)Math.Ceiling(iterations * 0.95) - 1;
        var p95Ms = durations[Math.Min(p95Index, durations.Count - 1)];

        Assert.That(
            p95Ms,
            Is.LessThan(20),
            $"GetPropertiesAsync p95 latency is {p95Ms} ms — must be < 20 ms (FX6-A5). " +
            $"All durations (ms): [{string.Join(", ", durations)}]");
    }

    // -------------------------------------------------------------------------
    // FX6-A5: GetPropertiesAsync p95 < 50ms with includeDefaults=true (legacy path)
    // -------------------------------------------------------------------------

    [Test]
    public void GetPropertiesAsync_IncludeDefaultsTrue_P95Under50Ms()
    {
        // Warm up.
        var rootId = this.GetRootNodeId();
        this.inspector.GetPropertiesAsync(rootId, null, null, includeDefaults: true, null, 50, default)
            .GetAwaiter().GetResult();

        // Measure 20 iterations.
        const int iterations = 20;
        var durations = new List<long>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            this.inspector.GetPropertiesAsync(rootId, null, null, includeDefaults: true, null, 50, default)
                .GetAwaiter().GetResult();
            sw.Stop();
            durations.Add(sw.ElapsedMilliseconds);
        }

        durations.Sort();
        var p95Index = (int)Math.Ceiling(iterations * 0.95) - 1;
        var p95Ms = durations[Math.Min(p95Index, durations.Count - 1)];

        // Legacy full-scan path is allowed up to 50ms p95.
        Assert.That(
            p95Ms,
            Is.LessThan(50),
            $"GetPropertiesAsync (includeDefaults=true) p95 latency is {p95Ms} ms — must be < 50 ms. " +
            $"All durations (ms): [{string.Join(", ", durations)}]");
    }

    // -------------------------------------------------------------------------
    // FX6-A5: GetPropertiesAsync correctness — returns at least one property
    // -------------------------------------------------------------------------

    [Test]
    public void GetPropertiesAsync_ReturnsProperties()
    {
        var rootId = this.GetRootNodeId();
        var page = this.inspector.GetPropertiesAsync(
                rootId, null, null, includeDefaults: true, null, 50, default)
            .GetAwaiter().GetResult();

        Assert.That(page.Items, Is.Not.Empty,
            "GetPropertiesAsync must return at least one property for a Button.");
        Assert.That(page.TotalCount, Is.GreaterThan(0),
            "TotalCount must be > 0 for a Button.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string GetRootNodeId()
    {
        var tree = this.inspector.GetVisualTreeAsync(
                rootNodeId: null,
                maxDepth: 1,
                treeType: "visual",
                includeProperties: null,
                ct: default)
            .GetAwaiter().GetResult();

        return tree.Root.NodeId;
    }
}
