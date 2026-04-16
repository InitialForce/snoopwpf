// S-3 Tree-change detection benchmark
// WPF requires STA thread.  We use an explicit static Main() with [STAThread].

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using SnoopWPF.Agent.Engine.Infrastructure;

namespace TreeChangeBench;

internal static class Program
{
    // -----------------------------------------------------------------------
    // Benchmark configuration
    // -----------------------------------------------------------------------
    private const int NodeCount = 200;
    private const int WarmupOps = 50_000;
    private const int BenchOps = 1_000_000;

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------
    [STAThread]
    static void Main()
    {
        // WPF requires an Application object on the STA thread.
        // We never call Application.Run() — we just need the plumbing.
        var app = new Application();
        _ = app; // suppress CS0219

        Console.WriteLine("=== S-3 Tree-change detection benchmark ===");
        Console.WriteLine($"Nodes: {NodeCount}, Ops per call: {BenchOps:N0}");
        Console.WriteLine();

        var (nodes, registry) = BuildTree(NodeCount);

        var results = new Dictionary<string, BenchResult>();

        // --- Scenario 1: Pure NodeRegistry.GetOrCreateId() hot-loop ----------
        results["Bump"] = RunScenario(
            "Bump (GetOrCreateId hot-loop)",
            WarmupOps,
            BenchOps,
            () =>
            {
                // Each "op" is a single GetOrCreateId call on the first node
                // (already registered → fast-path only)
                registry.GetOrCreateId(nodes[0]);
            });

        // --- Scenario 2: Panel.Children.Add/Remove + CollectionChanged --------
        // We measure the cost of adding/removing a single child from a StackPanel
        // while a Loaded handler calls GetOrCreateId, approximating the
        // CollectionChanged→Bump pipeline cost.
        {
            var panel = new StackPanel();
            var testBtn = new Button { Content = "test" };
            testBtn.Loaded += (s, _) => registry.GetOrCreateId(s!);

            results["CollectionChanged"] = RunScenario(
                "Panel.Children.Add/Remove (→Bump via Loaded)",
                WarmupOps,
                BenchOps,
                () =>
                {
                    panel.Children.Add(testBtn);
                    panel.Children.Remove(testBtn);
                });
        }

        // --- Scenario 3: FrameworkElement.Loaded/Unloaded --------------------
        // Measure subscription overhead: attach+detach a Loaded handler per node.
        {
            RoutedEventHandler handler = (s, _) => registry.GetOrCreateId(s!);

            results["LoadedUnloaded"] = RunScenario(
                "FrameworkElement.AddHandler/RemoveHandler (Loaded event)",
                WarmupOps,
                BenchOps,
                () =>
                {
                    var node = nodes[0];
                    node.AddHandler(FrameworkElement.LoadedEvent, handler);
                    node.RemoveHandler(FrameworkElement.LoadedEvent, handler);
                });
        }

        // --- Scenario 4: CompositionTarget.Rendering frame-tick walk ----------
        // Simulate a per-frame walk over all 200 nodes (budget-capped to 200 µs).
        results["Rendering"] = RunScenario(
            "CompositionTarget.Rendering frame-tick walk (200 nodes)",
            WarmupOps / NodeCount,
            BenchOps / NodeCount,
            () =>
            {
                foreach (var node in nodes)
                {
                    registry.GetOrCreateId(node);
                }
            });

        registry.Dispose();

        // -----------------------------------------------------------------------
        // Print results table
        // -----------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("| Scenario                                         | Mean (ns) | P50 (ns) | P95 (ns) | P99 (ns) |");
        Console.WriteLine("|--------------------------------------------------|-----------|----------|----------|----------|");

        foreach (var (name, r) in results)
        {
            Console.WriteLine($"| {name,-48} | {r.MeanNs,9:F1} | {r.P50Ns,8:F1} | {r.P95Ns,8:F1} | {r.P99Ns,8:F1} |");
        }

        Console.WriteLine();

        // Verdict logic: target is < 1 µs (1000 ns) per Bump
        var bumpMean = results["Bump"].MeanNs;
        var ccMean = results["CollectionChanged"].MeanNs;
        var luMean = results["LoadedUnloaded"].MeanNs;
        var renderMean = results["Rendering"].MeanNs;

        string verdict;
        string decision;

        if (bumpMean < 500 && ccMean < 1_000_000 && luMean < 1_000_000 && renderMean < 1_000_000)
        {
            verdict = "GREEN";
            decision = "ring-buffer not needed — Bump well under 500 ns/op";
        }
        else if (bumpMean < 1000)
        {
            verdict = "YELLOW";
            decision = $"ring-buffer not needed for Bump ({bumpMean:F1} ns) but monitor CollectionChanged and Loaded under load";
        }
        else
        {
            verdict = "RED";
            decision = $"ring-buffer needed — Bump exceeds 1 µs ({bumpMean:F1} ns)";
        }

        Console.WriteLine($"VERDICT: {verdict}");
        Console.WriteLine($"Decision: {decision}");

        // Machine-readable markers for acceptance criteria grep
        Console.WriteLine();
        Console.WriteLine($"Bump {bumpMean:F1}ns | CollectionChanged {ccMean:F1}ns | Loaded/Unloaded {luMean:F1}ns | Rendering {renderMean:F1}ns");

        // Structured block for SPIKE-RESULTS.md injection
        Console.WriteLine();
        Console.WriteLine("--- SPIKE-RESULTS-BLOCK-START ---");
        Console.WriteLine($"| Bump (GetOrCreateId hot-loop)                    | {results["Bump"].MeanNs,9:F1} | {results["Bump"].P50Ns,8:F1} | {results["Bump"].P95Ns,8:F1} | {results["Bump"].P99Ns,8:F1} |");
        Console.WriteLine($"| Panel.Children.Add/Remove (CollectionChanged→Bump)| {results["CollectionChanged"].MeanNs,9:F1} | {results["CollectionChanged"].P50Ns,8:F1} | {results["CollectionChanged"].P95Ns,8:F1} | {results["CollectionChanged"].P99Ns,8:F1} |");
        Console.WriteLine($"| FrameworkElement.Loaded/Unloaded subscription    | {results["LoadedUnloaded"].MeanNs,9:F1} | {results["LoadedUnloaded"].P50Ns,8:F1} | {results["LoadedUnloaded"].P95Ns,8:F1} | {results["LoadedUnloaded"].P99Ns,8:F1} |");
        Console.WriteLine($"| CompositionTarget.Rendering frame-tick walk      | {results["Rendering"].MeanNs,9:F1} | {results["Rendering"].P50Ns,8:F1} | {results["Rendering"].P95Ns,8:F1} | {results["Rendering"].P99Ns,8:F1} |");
        Console.WriteLine("--- SPIKE-RESULTS-BLOCK-END ---");
        Console.WriteLine($"FINAL-VERDICT:{verdict}");
        Console.WriteLine($"FINAL-DECISION:{decision}");
        Console.WriteLine($"BUMP-MEAN-NS:{bumpMean:F1}");
        Console.WriteLine($"CC-MEAN-NS:{ccMean:F1}");
        Console.WriteLine($"LU-MEAN-NS:{luMean:F1}");
        Console.WriteLine($"RENDER-MEAN-NS:{renderMean:F1}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (List<FrameworkElement> nodes, NodeRegistry registry) BuildTree(int count)
    {
        var registry = new NodeRegistry(TimeSpan.FromMinutes(5));
        var nodes = new List<FrameworkElement>(count);

        // Build a realistic nested tree: Grid → StackPanels → Buttons
        var root = new Grid();
        nodes.Add(root);

        var currentStack = new StackPanel();
        root.Children.Add(currentStack);
        nodes.Add(currentStack);

        while (nodes.Count < count)
        {
            var btn = new Button { Content = $"Node {nodes.Count}" };
            currentStack.Children.Add(btn);
            nodes.Add(btn);

            if (nodes.Count < count && nodes.Count % 20 == 0)
            {
                // Occasionally nest a StackPanel for realism
                var nested = new StackPanel();
                currentStack.Children.Add(nested);
                nodes.Add(nested);
                currentStack = nested;
            }
        }

        // Pre-register all nodes (warm up the registry)
        foreach (var node in nodes)
        {
            registry.GetOrCreateId(node);
        }

        return (nodes, registry);
    }

    private static BenchResult RunScenario(string name, int warmup, int ops, Action action)
    {
        Console.Write($"  {name} ... ");

        // Warm up
        for (int i = 0; i < warmup; i++)
        {
            action();
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // Collect individual iteration timings
        var samples = new double[ops];
        var sw = new Stopwatch();
        double ticksPerNs = Stopwatch.Frequency / 1_000_000_000.0;

        for (int i = 0; i < ops; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            samples[i] = sw.ElapsedTicks / ticksPerNs;
        }

        Array.Sort(samples);

        double mean = 0;
        foreach (var v in samples) mean += v;
        mean /= samples.Length;

        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var p99 = Percentile(samples, 0.99);

        Console.WriteLine($"mean={mean:F1}ns  p50={p50:F1}ns  p95={p95:F1}ns  p99={p99:F1}ns");

        return new BenchResult(mean, p50, p95, p99);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        double idx = p * (sorted.Length - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double frac = idx - lo;
        return sorted[lo] * (1 - frac) + sorted[hi] * frac;
    }

    private record BenchResult(double MeanNs, double P50Ns, double P95Ns, double P99Ns);
}
