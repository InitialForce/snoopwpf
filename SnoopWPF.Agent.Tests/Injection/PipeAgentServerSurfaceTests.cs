namespace SnoopWPF.Agent.Tests.Injection;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Injection;
using SnoopWPF.Agent.Tests.Fakes;

/// <summary>
/// Surface test: verifies that <see cref="PipeAgentServer"/> dispatch table covers every method
/// name that <c>PipeSnoopInspectorProxy</c> can send over the pipe. This is the regression guard
/// for FX6-F (bd-1we.6.1) — any new method added to the proxy without a matching dispatch entry
/// will cause this test to fail with a clear list of missing method names.
/// </summary>
[TestFixture]
public sealed class PipeAgentServerSurfaceTests
{
    // ----------------------------------------------------------------
    // Canonical set of method names sent by PipeSnoopInspectorProxy.
    // Mirror of every InvokeAsync("MethodName", ...) call in
    // SnoopWPF.Agent.Remote/PipeSnoopInspectorProxy.cs.
    // ----------------------------------------------------------------

    private static readonly string[] ExpectedMethodNames =
    {
        // ── Original 18 (nodeId-based) ──────────────────────────────
        "GetSessionInfo",
        "GetWindows",
        "GetVisualTree",
        "GetChildren",
        "GetAncestors",
        "FindElements",
        "InspectElement",
        "GetProperties",
        "SetProperty",
        "GetBindingInfo",
        "RunDiagnostics",
        "GetResources",
        "CaptureScreenshot",
        "GetTriggers",
        "GetBehaviors",
        "SetTextValue",
        "ExecuteCommand",
        "SetSliderValue",

        // ── WpfLocator overloads (ByLocator suffix) ──────────────────
        "GetVisualTreeByLocator",
        "GetChildrenByLocator",
        "GetAncestorsByLocator",
        "InspectElementByLocator",
        "GetPropertiesByLocator",
        "SetPropertyByLocator",
        "GetBindingInfoByLocator",
        "RunDiagnosticsByLocator",
        "GetResourcesByLocator",
        "CaptureScreenshotByLocator",
        "GetTriggersByLocator",
        "GetBehaviorsByLocator",

        // ── New nodeId methods ───────────────────────────────────────
        "SelectItem",
        "SelectItemByLocator",
        "SetCheckState",
        "SetCheckStateByLocator",
        "SetTextValueByLocator",
        "SetSliderValueByLocator",
        "ExecuteCommandByLocator",
        "Click",
        "ClickByLocator",
        "Toggle",
        "ToggleByLocator",
        "ExpandCollapse",
        "ExpandCollapseByLocator",
        "ResolveBinding",
        "ResolveBindingByLocator",

        // ── New operations (M2-08/09/10/11) ─────────────────────────
        "WaitForProperty",
        "PollChanges",
        "PumpUntilIdle",
    };

    [Test]
    public void DispatchCoversAllInterfaceMethods()
    {
        // Arrange — build a PipeAgentServer with the fake inspector so we can
        // extract the dispatch table without needing a real WPF Dispatcher.
        var fake = new FakeSnoopInspector();
        var dummyToken = new byte[32];
        var server = new PipeAgentServer("test-pipe-surface", dummyToken, fake);

        // Invoke the private BuildDispatchTable method via reflection.
        var buildMethod = typeof(PipeAgentServer).GetMethod(
            "BuildDispatchTable",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(
            buildMethod,
            Is.Not.Null,
            "PipeAgentServer.BuildDispatchTable() must be accessible via reflection for this surface test.");

        var dispatchTable = buildMethod!.Invoke(server, null)
            as Dictionary<string, Func<string, CancellationToken, System.Threading.Tasks.Task<string>>>;

        Assert.That(
            dispatchTable,
            Is.Not.Null,
            "BuildDispatchTable() must return a non-null Dictionary.");

        // Act — diff expected vs actual keys (case-insensitive, matching the OrdinalIgnoreCase comparer used at runtime).
        var actual = new HashSet<string>(dispatchTable!.Keys, StringComparer.OrdinalIgnoreCase);
        var missing = ExpectedMethodNames
            .Where(m => !actual.Contains(m))
            .OrderBy(m => m)
            .ToList();

        // Assert — fail loudly with the full list of missing method names.
        Assert.That(
            missing,
            Is.Empty,
            "PipeAgentServer.DispatchAsync is missing dispatch cases for the following method names "
            + $"(as sent by PipeSnoopInspectorProxy):\n  {string.Join("\n  ", missing)}\n\n"
            + "Add a matching case to BuildDispatchTable() in PipeAgentServer.cs for each missing method.");
    }

    [Test]
    public void DispatchTableHasNoSpuriousEntries()
    {
        // Inverse guard: no dispatch entry should exist that the proxy would NEVER call.
        // This catches dead code (stale dispatch entries left after a method rename).
        var fake = new FakeSnoopInspector();
        var dummyToken = new byte[32];
        var server = new PipeAgentServer("test-pipe-surface-inv", dummyToken, fake);

        var buildMethod = typeof(PipeAgentServer).GetMethod(
            "BuildDispatchTable",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var dispatchTable = buildMethod.Invoke(server, null)
            as Dictionary<string, Func<string, CancellationToken, System.Threading.Tasks.Task<string>>>;

        var expected = new HashSet<string>(ExpectedMethodNames, StringComparer.OrdinalIgnoreCase);
        var spurious = dispatchTable!.Keys
            .Where(k => !expected.Contains(k))
            .OrderBy(k => k)
            .ToList();

        Assert.That(
            spurious,
            Is.Empty,
            "PipeAgentServer.BuildDispatchTable() contains entries that are NOT in the expected "
            + $"proxy method name list:\n  {string.Join("\n  ", spurious)}\n\n"
            + "Either add the method name to ExpectedMethodNames in this test (if it's a new proxy method) "
            + "or remove the stale dispatch entry from PipeAgentServer.cs.");
    }
}
