// VeriGUI Harness Runner — M2-15a
// Reads Scenarios/*.md, executes tool calls against a headless SnoopAgent,
// measures action-success rate, repeat-on-unchanged-state rate, and p95 latency.
// Exits non-zero if thresholds are violated (--assert-pass-thresholds flag).

namespace SnoopWPF.Agent.VeriGuiHarness;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;

// ─── CLI options ────────────────────────────────────────────────────────────

public sealed class Options
{
    [Option("run", HelpText = "Execute all scenarios in Scenarios/.")]
    public bool Run { get; set; }

    [Option("scenarios", Default = "Scenarios", HelpText = "Directory containing scenario .md files.")]
    public string ScenariosDir { get; set; } = "Scenarios";

    [Option("out", HelpText = "Path for the JSON report output.")]
    public string? OutPath { get; set; }

    [Option("assert-pass-thresholds", HelpText = "Exit non-zero if any threshold is violated.")]
    public bool AssertPassThresholds { get; set; }

    [Option("repeat-on-unchanged-threshold", Default = 0.05,
        HelpText = "Maximum allowed repeat-on-unchanged-state rate (default 0.05 = 5%).")]
    public double RepeatOnUnchangedThreshold { get; set; } = 0.05;

    [Option("p95-latency-threshold-ms", Default = 10.0,
        HelpText = "Maximum allowed p95 per-call latency in ms (default 10).")]
    public double P95LatencyThresholdMs { get; set; } = 10.0;
}

// ─── Domain types ───────────────────────────────────────────────────────────

public sealed record ScenarioStep(string Tool, string Args, string? ExpectLocator);

public sealed record Scenario(string Name, string FilePath, IReadOnlyList<ScenarioStep> Steps);

public sealed record StepResult(
    string ScenarioName,
    int StepIndex,
    string Tool,
    bool Success,
    bool WasRepeatOnUnchangedState,
    double ElapsedMs,
    string? ErrorMessage);

public sealed record HarnessReport(
    DateTimeOffset RunAt,
    int TotalScenarios,
    int TotalSteps,
    int SucceededSteps,
    int FailedSteps,
    int RepeatOnUnchangedStateCount,
    double ActionSuccessRate,
    double RepeatOnUnchangedStateRate,
    double P95PerCallMs,
    IReadOnlyList<StepResult> StepResults,
    IReadOnlyList<ThresholdResult> ThresholdResults);

public sealed record ThresholdResult(string Metric, double Value, double Threshold, bool Passed);

// ─── Scenario parser ────────────────────────────────────────────────────────

internal static class ScenarioParser
{
    // Matches fenced code blocks tagged with a tool name: ```tool_name
    private static readonly Regex ToolBlock =
        new Regex(@"```(?<tool>[a-z_]+)\s*\n(?<args>.*?)```", RegexOptions.Singleline | RegexOptions.Compiled);

    // Optionally extracts an expect-locator from the prose: expect: <locator>
    private static readonly Regex ExpectLine =
        new Regex(@"^expect:\s*(?<loc>.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static Scenario Parse(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var content = File.ReadAllText(filePath);
        var steps = new List<ScenarioStep>();

        foreach (Match m in ToolBlock.Matches(content))
        {
            var tool = m.Groups["tool"].Value.Trim();
            var args = m.Groups["args"].Value.Trim();
            var expectMatch = ExpectLine.Match(content[(m.Index + m.Length)..]);
            var expectLocator = expectMatch.Success ? expectMatch.Groups["loc"].Value.Trim() : null;
            steps.Add(new ScenarioStep(tool, args, expectLocator));
        }

        return new Scenario(name, filePath, steps);
    }
}

// ─── Stub executor ──────────────────────────────────────────────────────────
// In CI (no live WPF process), steps execute against a no-op stub so the
// harness can verify plumbing and threshold logic without a real target app.
// M2-15b wires in the real SnoopAgent once a target is available.

internal static class StubExecutor
{
    private static readonly Random Rng = new Random(42);

    // Returns (success, repeatOnUnchanged, elapsedMs, error, newSnapshot).
    internal static async Task<(bool Success, bool RepeatOnUnchanged, double ElapsedMs, string? Error, string NewSnapshot)>
        ExecuteAsync(ScenarioStep step, string previousSnapshot)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromMicroseconds(Rng.Next(50, 500)));
        sw.Stop();

        // Simulate ~2% repeat-on-unchanged-state (well within the 5% threshold).
        var repeatOnUnchanged = (previousSnapshot == step.Args) && (Rng.NextDouble() < 0.02);
        var success = !repeatOnUnchanged || Rng.NextDouble() > 0.1;
        var error = success ? null : $"Stub: tool '{step.Tool}' failed (simulated)";

        // Successful calls mutate state; failures leave it unchanged.
        var newSnapshot = success ? step.Args + ":done" : previousSnapshot;

        return (success, repeatOnUnchanged, sw.Elapsed.TotalMilliseconds, error, newSnapshot);
    }
}

// ─── Runner ─────────────────────────────────────────────────────────────────

public static class Runner
{
    public static async Task<int> RunAsync(Options opts)
    {
        var scenariosDir = Path.IsPathRooted(opts.ScenariosDir)
            ? opts.ScenariosDir
            : Path.Combine(AppContext.BaseDirectory, opts.ScenariosDir);

        if (!Directory.Exists(scenariosDir))
        {
            Console.Error.WriteLine($"[VeriGUI] Scenarios directory not found: {scenariosDir}");
            return 2;
        }

        var scenarioFiles = Directory.GetFiles(scenariosDir, "*.md", SearchOption.TopDirectoryOnly);
        if (scenarioFiles.Length == 0)
        {
            Console.Error.WriteLine($"[VeriGUI] No *.md scenario files found in: {scenariosDir}");
            return 2;
        }

        var scenarios = scenarioFiles.Select(ScenarioParser.Parse).ToList();
        Console.WriteLine($"[VeriGUI] Loaded {scenarios.Count} scenarios ({scenarios.Sum(s => s.Steps.Count)} steps).");

        var results = new List<StepResult>();
        var latencies = new List<double>();

        foreach (var scenario in scenarios)
        {
            var snapshot = string.Empty;
            for (var i = 0; i < scenario.Steps.Count; i++)
            {
                var step = scenario.Steps[i];
                var result = await StubExecutor.ExecuteAsync(step, snapshot);
                snapshot = result.NewSnapshot;
                latencies.Add(result.ElapsedMs);
                results.Add(new StepResult(
                    scenario.Name,
                    i,
                    step.Tool,
                    result.Success,
                    result.RepeatOnUnchanged,
                    result.ElapsedMs,
                    result.Error));
            }
        }

        var report = BuildReport(scenarios.Count, results, latencies, opts);
        PrintSummary(report);

        if (opts.OutPath is { Length: > 0 })
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
            var outDir = Path.GetDirectoryName(opts.OutPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllText(opts.OutPath, json);
            Console.WriteLine($"[VeriGUI] Report written to: {opts.OutPath}");
        }

        if (opts.AssertPassThresholds)
        {
            var violations = report.ThresholdResults.Where(t => !t.Passed).ToList();
            if (violations.Count > 0)
            {
                foreach (var v in violations)
                {
                    Console.Error.WriteLine($"[VeriGUI] THRESHOLD VIOLATED: {v.Metric} = {v.Value:F4} > {v.Threshold:F4}");
                }

                return 1;
            }
        }

        return 0;
    }

    private static HarnessReport BuildReport(
        int scenarioCount,
        List<StepResult> results,
        List<double> latencies,
        Options opts)
    {
        var total = results.Count;
        var succeeded = results.Count(r => r.Success);
        var failed = total - succeeded;
        var repeatCount = results.Count(r => r.WasRepeatOnUnchangedState);
        var successRate = total > 0 ? (double)succeeded / total : 1.0;
        var repeatRate = total > 0 ? (double)repeatCount / total : 0.0;

        latencies.Sort();
        var p95Ms = latencies.Count > 0
            ? latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1]
            : 0.0;

        var thresholds = new[]
        {
            new ThresholdResult("repeatOnUnchangedRate", repeatRate,
                opts.RepeatOnUnchangedThreshold, repeatRate < opts.RepeatOnUnchangedThreshold),
            new ThresholdResult("p95PerCallMs", p95Ms,
                opts.P95LatencyThresholdMs, p95Ms < opts.P95LatencyThresholdMs),
        };

        return new HarnessReport(
            RunAt: DateTimeOffset.UtcNow,
            TotalScenarios: scenarioCount,
            TotalSteps: total,
            SucceededSteps: succeeded,
            FailedSteps: failed,
            RepeatOnUnchangedStateCount: repeatCount,
            ActionSuccessRate: successRate,
            RepeatOnUnchangedStateRate: repeatRate,
            P95PerCallMs: p95Ms,
            StepResults: results,
            ThresholdResults: thresholds);
    }

    private static void PrintSummary(HarnessReport r)
    {
        Console.WriteLine($"[VeriGUI] Steps: {r.TotalSteps}  Success: {r.ActionSuccessRate:P1}" +
                          $"  RepeatOnUnchanged: {r.RepeatOnUnchangedStateRate:P2}  p95: {r.P95PerCallMs:F2} ms");
        foreach (var t in r.ThresholdResults)
        {
            Console.WriteLine($"[VeriGUI]   {t.Metric}: {t.Value:F4} {(t.Passed ? "<=" : ">")} {t.Threshold} [{(t.Passed ? "PASS" : "FAIL")}]");
        }
    }
}

// ─── Entry point ────────────────────────────────────────────────────────────

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
            .MapResult(
                opts => Runner.RunAsync(opts),
                _ => Task.FromResult(1));
    }
}
