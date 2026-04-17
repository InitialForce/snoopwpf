namespace SnoopWPF.Agent.Tests.Fakes;

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Unit tests for <see cref="FakeSnoopInspector"/> strict mode (FX6-I2).
/// </summary>
[TestFixture]
public class FakeSnoopInspectorTests
{
    // ── StrictMode = false (default) — unconfigured calls return defaults ────────

    [Test]
    public async Task StrictMode_False_Click_ReturnsDefaultSuccess()
    {
        var fake = new FakeSnoopInspector(); // StrictMode = false by default
        var result = await fake.ClickAsync("0:1", CancellationToken.None);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task StrictMode_False_Toggle_ReturnsDefaultSuccess()
    {
        var fake = new FakeSnoopInspector();
        var result = await fake.ToggleAsync("0:1", CancellationToken.None);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task StrictMode_False_ExpandCollapse_ReturnsDefaultSuccess()
    {
        var fake = new FakeSnoopInspector();
        var result = await fake.ExpandCollapseAsync("0:1", "expand", CancellationToken.None);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task StrictMode_False_ResolveBinding_ReturnsNoBinding()
    {
        var fake = new FakeSnoopInspector();
        var result = await fake.ResolveBindingAsync("0:1", "Text", CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo("NoBinding"));
    }

    [Test]
    public async Task StrictMode_False_PollChanges_ReturnsEchoVersion()
    {
        var fake = new FakeSnoopInspector();
        var result = await fake.PollChangesAsync(42, null, CancellationToken.None);
        Assert.That(result.TreeVersion, Is.EqualTo(42));
        Assert.That(result.SinceVersion, Is.EqualTo(42));
    }

    [Test]
    public async Task StrictMode_False_PumpUntilIdle_ReturnsIdleReached()
    {
        var fake = new FakeSnoopInspector();
        var result = await fake.PumpUntilIdleAsync(5000, null, CancellationToken.None);
        Assert.That(result.IdleReached, Is.True);
    }

    // ── StrictMode = true — unconfigured calls throw ────────────────────────────

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_Click()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.ClickAsync("0:1", CancellationToken.None));
    }

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_Toggle()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.ToggleAsync("0:1", CancellationToken.None));
    }

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_ExpandCollapse()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.ExpandCollapseAsync("0:1", "expand", CancellationToken.None));
    }

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_ResolveBinding()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.ResolveBindingAsync("0:1", "Text", CancellationToken.None));
    }

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_PollChanges()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.PollChangesAsync(0, null, CancellationToken.None));
    }

    [Test]
    public void StrictMode_ThrowsOnUnhandledCall_PumpUntilIdle()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.PumpUntilIdleAsync(5000, null, CancellationToken.None));
    }

    // ── StrictMode = true — configured calls work normally ──────────────────────

    [Test]
    public async Task StrictMode_ConfiguredDelegate_UsedInsteadOfThrowing()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        fake.OnClick = (nodeId, ct) =>
            Task.FromResult(new StateDeltaDto { Success = true, StateChanged = true });

        var result = await fake.ClickAsync("0:1", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StateChanged, Is.True);
    }

    // ── StrictMode error message includes method name ────────────────────────────

    [Test]
    public void StrictMode_ErrorMessage_IncludesMethodName_Click()
    {
        var fake = new FakeSnoopInspector { StrictMode = true };
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.ClickAsync("0:1", CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("OnClick"));
        Assert.That(ex.Message, Does.Contain("strict mode"));
    }
}
