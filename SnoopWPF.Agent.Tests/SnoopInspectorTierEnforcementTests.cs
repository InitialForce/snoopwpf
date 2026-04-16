namespace SnoopWPF.Agent.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// FX-M8: Verifies that all 9 SnoopInspector mutation methods return TierMismatch when
/// SessionPolicy.MaxTier is L0ReadOnly, even when EnableMutation=true.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "NUnit [TearDown] disposes inspector after each test.")]
public class SnoopInspectorTierEnforcementTests
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;
    private SnoopInspector inspector = null!;

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
            Name = "TierEnforcement-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        // Build a policy with MaxTier=L0ReadOnly but EnableMutation=true —
        // the tier check must override EnableMutation.
        var policy = new SessionPolicy
        {
            Mode = SessionMode.CoLocated,
            MaxTier = InputTier.L0ReadOnly,
            EnableMutation = true,
            EnableAutomation = true,
            EnableRedaction = false,
        };

        this.inspector = new SnoopInspector(
            this.dispatcher,
            options: new SnoopInspectorOptions
            {
                EnableMutation = true,
                EnableAutomation = true,
                TimeoutMs = 3000,
            },
            sessionPolicy: policy);
    }

    [TearDown]
    public void TearDown()
    {
        this.inspector.Dispose();
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task SetPropertyAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.SetPropertyAsync("any-node", "Width", "100", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task SetCheckStateAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.SetCheckStateAsync("any-node", "checked", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task SetTextValueAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.SetTextValueAsync("any-node", "hello", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task SetSliderValueAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.SetSliderValueAsync("any-node", 0.5, normalized: true, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task SelectItemAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.SelectItemAsync("any-node", "item-1", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task ClickAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.ClickAsync("any-node", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task ToggleAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.ToggleAsync("any-node", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task ExpandCollapseAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.ExpandCollapseAsync("any-node", "expand", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public async Task ExecuteCommandAsync_MaxTierL0ReadOnly_ReturnsTierMismatch()
    {
        var result = await this.inspector.ExecuteCommandAsync("any-node", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.TierMismatch));
    }
}
