namespace SnoopWPF.Agent.Tests.StateDelta;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX-M10: Verifies that all 5 mutation tools emit the canonical STATE_UNCHANGED shape
/// (PRD §7.6) when the mutation is a no-op: <c>success=true, stateChanged=false,
/// treeVersionDelta=0, failureReason=STATE_UNCHANGED, suggestion!=null</c>.
/// </summary>
/// <remarks>
/// Each test calls the real <see cref="SnoopInspector"/> against a WPF control wired
/// to reject mutations that would produce no net change (e.g. setting the same value
/// a second time).
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class StateChangedSerializationTests : IDisposable
{
    // ── Shared STA Dispatcher for the fixture ────────────────────────────────────

    private Thread? staThread;
    private Dispatcher? staDispatcher;
    private readonly ManualResetEventSlim dispatcherReady = new(initialState: false);

    // ── Fixture lifecycle ────────────────────────────────────────────────────────

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        this.staThread = new Thread(this.RunDispatcher)
        {
            Name = "StateChangedSerialization-STA",
            IsBackground = true,
        };
        this.staThread.SetApartmentState(ApartmentState.STA);
        this.staThread.Start();

        if (!this.dispatcherReady.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("STA Dispatcher did not start in time.");
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        this.staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
        this.staThread?.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        this.dispatcherReady.Dispose();
    }

    private void RunDispatcher()
    {
        this.staDispatcher = Dispatcher.CurrentDispatcher;
        this.dispatcherReady.Set();
        Dispatcher.Run();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private SnoopInspector CreateInspector(DependencyObject root) =>
        new SnoopInspector(
            dispatcher: this.staDispatcher!,
            rootTarget: root,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                // Generous acceptance window so a contended CI runner does not trip a
                // spurious DispatcherBusy with the 500 ms production default.
                DispatcherAcceptanceTimeoutMs = 5_000,
                EnableMutation = true,
                EnableRedaction = false,
            });

    private static string GetRootNodeId(SnoopInspector inspector)
    {
        var tree = inspector
            .GetVisualTreeAsync(
                rootNodeId: null,
                maxDepth: 1,
                treeType: "visual",
                includeProperties: null,
                ct: default)
            .GetAwaiter().GetResult();

        return tree.Root.NodeId;
    }

    /// <summary>
    /// Asserts that a result carries the canonical STATE_UNCHANGED shape per PRD §7.6.
    /// </summary>
    private static void AssertStateUnchangedShape(Contracts.Dtos.StateDeltaDto result, string context)
    {
        Assert.That(result.Success, Is.True,
            $"[{context}] success must be true for STATE_UNCHANGED (PRD §7.6).");
        Assert.That(result.StateChanged, Is.False,
            $"[{context}] stateChanged must be false for a no-op.");
        Assert.That(result.TreeVersionDelta, Is.EqualTo(0),
            $"[{context}] treeVersionDelta must be 0 for a no-op.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.StateUnchanged),
            $"[{context}] failureReason must be StateUnchanged so the agent can distinguish no-op from success.");
        Assert.That(result.Suggestion, Is.Not.Null,
            $"[{context}] suggestion must be populated so the agent can act without human intervention.");
    }

    // ── 1. SetPropertyAsync ──────────────────────────────────────────────────────

    /// <summary>
    /// When SetProperty is called with the same value already in the DP, the settled
    /// observable state is unchanged — the result must be STATE_UNCHANGED per PRD §7.6.
    /// </summary>
    [Test]
    public void SetPropertyAsync_NoOp_ReturnsStateUnchangedShape()
    {
        var dispatcher = this.staDispatcher!;

        // Use a plain TextBox whose Width starts at 100.
        var subject = dispatcher.Invoke(() =>
        {
            var tb = new TextBox();
            tb.Width = 100.0;
            return tb;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        // First set to 100 (same as current) — must be a no-op.
        var result = inspector
            .SetPropertyAsync(nodeId, "Width", "100", ct: default)
            .GetAwaiter().GetResult();

        AssertStateUnchangedShape(result, "SetPropertyAsync");
    }

    // ── 2. SetCheckStateAsync ────────────────────────────────────────────────────

    /// <summary>
    /// Calling SetCheckState with the same check state as the current value must
    /// return STATE_UNCHANGED per PRD §7.6.
    /// </summary>
    [Test]
    public void SetCheckStateAsync_NoOp_ReturnsStateUnchangedShape()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() =>
        {
            var cb = new CheckBox { IsChecked = true };
            return cb;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        // Already checked — setting "checked" again is a no-op.
        var result = inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .GetAwaiter().GetResult();

        AssertStateUnchangedShape(result, "SetCheckStateAsync");
    }

    // ── 3. SetTextValueAsync (TextBox) ───────────────────────────────────────────

    /// <summary>
    /// Calling SetTextValue with the same text already in the TextBox must return
    /// STATE_UNCHANGED per PRD §7.6.
    /// </summary>
    [Test]
    public void SetTextValueAsync_TextBox_NoOp_ReturnsStateUnchangedShape()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() =>
        {
            var tb = new TextBox { Text = "hello" };
            return tb;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        // Already "hello" — setting "hello" again is a no-op.
        var result = inspector
            .SetTextValueAsync(nodeId, "hello", ct: default)
            .GetAwaiter().GetResult();

        AssertStateUnchangedShape(result, "SetTextValueAsync(TextBox)");
    }

    // ── 4. SetSliderValueAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Calling SetSliderValue with the exact same value already on the Slider must
    /// return STATE_UNCHANGED per PRD §7.6.
    /// </summary>
    [Test]
    public void SetSliderValueAsync_NoOp_ReturnsStateUnchangedShape()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() =>
        {
            var slider = new Slider
            {
                Minimum = 0.0,
                Maximum = 100.0,
                Value = 50.0,
            };
            return slider;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        // Already 50 — setting 50 again is a no-op.
        var result = inspector
            .SetSliderValueAsync(nodeId, value: 50.0, normalized: false, ct: default)
            .GetAwaiter().GetResult();

        AssertStateUnchangedShape(result, "SetSliderValueAsync");
    }

    // ── 5. SelectItemAsync ───────────────────────────────────────────────────────

    /// <summary>
    /// Calling SelectItem with the index that is already selected must return
    /// STATE_UNCHANGED per PRD §7.6.
    /// </summary>
    [Test]
    public void SelectItemAsync_NoOp_ReturnsStateUnchangedShape()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() =>
        {
            var lb = new ListBox();
            lb.Items.Add("Alpha");
            lb.Items.Add("Beta");
            lb.Items.Add("Gamma");
            lb.SelectedIndex = 1;
            return lb;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        // Index 1 ("Beta") is already selected — re-selecting it is a no-op.
        var result = inspector
            .SelectItemAsync(nodeId, "1", ct: default)
            .GetAwaiter().GetResult();

        AssertStateUnchangedShape(result, "SelectItemAsync");
    }

    // ── Positive controls (ensure changed mutations still report success/stateChanged=true) ──

    [Test]
    public void SetCheckStateAsync_GenuineChange_ReturnsSuccessAndStateChanged()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() => new CheckBox { IsChecked = false });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        var result = inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.Success, Is.True, "success must be true on genuine change.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true on genuine change.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
    }

    [Test]
    public void SetTextValueAsync_GenuineChange_ReturnsSuccessAndStateChanged()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() => new TextBox { Text = "old" });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        var result = inspector
            .SetTextValueAsync(nodeId, "new", ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.Success, Is.True, "success must be true on genuine change.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true on genuine change.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
    }

    [Test]
    public void SetSliderValueAsync_GenuineChange_ReturnsSuccessAndStateChanged()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() => new Slider { Minimum = 0.0, Maximum = 100.0, Value = 10.0 });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        var result = inspector
            .SetSliderValueAsync(nodeId, value: 90.0, normalized: false, ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.Success, Is.True, "success must be true on genuine change.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true on genuine change.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
    }

    [Test]
    public void SelectItemAsync_GenuineChange_ReturnsSuccessAndStateChanged()
    {
        var dispatcher = this.staDispatcher!;

        var subject = dispatcher.Invoke(() =>
        {
            var lb = new ListBox();
            lb.Items.Add("Alpha");
            lb.Items.Add("Beta");
            lb.SelectedIndex = 0;
            return lb;
        });

        using var inspector = this.CreateInspector(subject);
        var nodeId = GetRootNodeId(inspector);

        var result = inspector
            .SelectItemAsync(nodeId, "1", ct: default)
            .GetAwaiter().GetResult();

        Assert.That(result.Success, Is.True, "success must be true on genuine change.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true on genuine change.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
    }
}
