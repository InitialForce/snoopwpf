namespace SnoopWPF.Agent.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Verifies M1-11: <c>stateChanged</c> is computed at response-serialization time by
/// reading the observable DP value after any re-entrant <see cref="PropertyChangedCallback"/>
/// has settled, not by comparing the requested value string (PRD §7.3 W3-C1).
/// </summary>
/// <remarks>
/// <para>
/// SnoopInspector dispatches WPF work to a dedicated Dispatcher and asserts that callers
/// must NOT be on that Dispatcher thread. A dedicated STA thread hosts the Dispatcher for
/// the whole fixture; test methods call the inspector from the MTA test thread.
/// </para>
/// <para>
/// The Dispatcher is shared across tests in this fixture to avoid per-test startup race
/// conditions when NUnit runs tests in parallel.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class StateChangedSerializationTests : IDisposable
{
    // ── Shared STA Dispatcher for the fixture ────────────────────────────────────

    private Thread? staThread;
    private Dispatcher? staDispatcher;
    private readonly ManualResetEventSlim dispatcherReady = new(initialState: false);

    // ── Subject types ────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal <see cref="DependencyObject"/> whose <see cref="PropertyChangedCallback"/>
    /// silently reverts any write back to the default (0).  Simulates the re-entrant
    /// false-positive: SetValue returns without error but the DP system has already
    /// applied the revert.
    /// </summary>
    private sealed class RevertingObject : DependencyObject
    {
        public static readonly DependencyProperty RevertingProperty =
            DependencyProperty.Register(
                nameof(Reverting),
                typeof(int),
                typeof(RevertingObject),
                new PropertyMetadata(
                    defaultValue: 0,
                    propertyChangedCallback: (d, e) =>
                    {
                        // Unconditionally revert any non-default write.
                        if ((int)e.NewValue != 0)
                        {
                            d.SetValue(RevertingProperty, 0);
                        }
                    }));

        public int Reverting
        {
            get => (int)this.GetValue(RevertingProperty);
            set => this.SetValue(RevertingProperty, value);
        }
    }

    /// <summary>
    /// A trivially mutable DP for the positive-control test.
    /// </summary>
    private sealed class MutableObject : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(int),
                typeof(MutableObject),
                new PropertyMetadata(defaultValue: 0));

        public int Value
        {
            get => (int)this.GetValue(ValueProperty);
            set => this.SetValue(ValueProperty, value);
        }
    }

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

    // ── Tests ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Core acceptance criterion (PRD §7.3 W3-C1):
    /// When a reverting <see cref="PropertyChangedCallback"/> resets the DP value,
    /// <c>stateChanged</c> must be <see langword="false"/> even though SetValue
    /// returned without throwing.
    /// </summary>
    [Test]
    public void RevertingCallback_SetValueSucceeds_StateChangedIsFalse()
    {
        var dispatcher = this.staDispatcher!;

        // Create the test subject on the STA thread (required for DependencyObject).
        RevertingObject subject = dispatcher.Invoke(() => new RevertingObject());

        using var inspector = new SnoopInspector(
            dispatcher: dispatcher,
            rootTarget: subject,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = true,
                EnableRedaction = false,
            });

        var nodeId = GetRootNodeId(inspector);

        // Try to set 42 — the reverting callback will immediately reset to 0.
        var result = inspector
            .SetPropertyAsync(nodeId, "Reverting", "42", ct: default)
            .GetAwaiter().GetResult();

        // Verify the observable state was actually reverted.
        int actual = dispatcher.Invoke(() => subject.Reverting);
        Assert.That(actual, Is.EqualTo(0),
            "Post-condition: reverting callback must have reset the value to 0.");

        // M1-11 / FX-M10 acceptance criterion (PRD §7.6).
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when a reverting callback resets the value.");
        Assert.That(result.Success, Is.True,
            "success must be true for STATE_UNCHANGED — the operation completed, it was a no-op (PRD §7.6).");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.StateUnchanged),
            "failureReason must be StateUnchanged.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "suggestion must be populated for STATE_UNCHANGED so the agent can act.");
        Assert.That(result.TreeVersionDelta, Is.EqualTo(0),
            "treeVersionDelta must be 0 for a no-op.");
    }

    /// <summary>
    /// Positive control: a DP without a reverting callback must still report
    /// <c>stateChanged=true</c> when the value genuinely changes.
    /// </summary>
    [Test]
    public void NormalDP_SetValueSucceeds_StateChangedIsTrue()
    {
        var dispatcher = this.staDispatcher!;

        MutableObject subject = dispatcher.Invoke(() => new MutableObject());

        using var inspector = new SnoopInspector(
            dispatcher: dispatcher,
            rootTarget: subject,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = true,
                EnableRedaction = false,
            });

        var nodeId = GetRootNodeId(inspector);

        var result = inspector
            .SetPropertyAsync(nodeId, "Value", "99", ct: default)
            .GetAwaiter().GetResult();

        int actual = dispatcher.Invoke(() => subject.Value);
        Assert.That(actual, Is.EqualTo(99),
            "Post-condition: value must be 99 after a successful set.");
        Assert.That(result.StateChanged, Is.True,
            "stateChanged must be true when the value genuinely changed.");
        Assert.That(result.Success, Is.True,
            "success must be true on a genuine state change.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the nodeId of the root target by walking the visual tree.
    /// </summary>
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
}
