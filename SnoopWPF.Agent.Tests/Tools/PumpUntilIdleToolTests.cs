namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="PumpUntilIdleTool"/>.
/// </summary>
[TestFixture]
public class PumpUntilIdleToolTests
{
    private FakeSnoopInspector fake = null!;

    private PumpUntilIdleTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new PumpUntilIdleTool(this.fake);
    }

    // ── Happy path: idle reached ────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_IdleReached_ReturnsResult()
    {
        this.fake.OnPumpUntilIdle = (timeoutMs, resources, ct) =>
            Task.FromResult(new PumpUntilIdleResultDto
            {
                IdleReached = true,
                ElapsedMs = 42,
                ResourcesMonitored = new List<string> { "Dispatcher", "CompositionRendering" },
                StillBusy = new List<string>(),
            });

        var json = await this.tool.PumpUntilIdleAsync(timeoutMs: 5000);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["idleReached"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["elapsedMs"]!.GetValue<int>(), Is.EqualTo(42));
    }

    // ── Happy path: timed out before idle ──────────────────────────────────────

    [Test]
    public async Task HappyPath_NotIdle_ReturnsBusyResources()
    {
        this.fake.OnPumpUntilIdle = (timeoutMs, resources, ct) =>
            Task.FromResult(new PumpUntilIdleResultDto
            {
                IdleReached = false,
                ElapsedMs = 5000,
                ResourcesMonitored = new List<string> { "Dispatcher", "CompositionRendering" },
                StillBusy = new List<string> { "CompositionRendering" },
            });

        var json = await this.tool.PumpUntilIdleAsync(timeoutMs: 5000);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["idleReached"]!.GetValue<bool>(), Is.False);
        var stillBusy = doc["stillBusy"]!.AsArray();
        Assert.That(stillBusy.Count, Is.EqualTo(1));
        Assert.That(stillBusy[0]!.GetValue<string>(), Is.EqualTo("CompositionRendering"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        int capturedTimeout = -1;
        IReadOnlyList<string>? capturedResources = null;

        this.fake.OnPumpUntilIdle = (timeoutMs, resources, ct) =>
        {
            capturedTimeout = timeoutMs;
            capturedResources = resources;
            return Task.FromResult(new PumpUntilIdleResultDto { IdleReached = true });
        };

        await this.tool.PumpUntilIdleAsync(timeoutMs: 2000, resources: new[] { "Dispatcher" });

        Assert.That(capturedTimeout, Is.EqualTo(2000));
        Assert.That(capturedResources, Is.Not.Null);
        Assert.That(capturedResources![0], Is.EqualTo("Dispatcher"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnPumpUntilIdle = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PumpUntilIdleResultDto { IdleReached = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.PumpUntilIdleAsync(timeoutMs: 5000, ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnPumpUntilIdle = (_, _, _) =>
            Task.FromResult(new PumpUntilIdleResultDto
            {
                IdleReached = true,
                ElapsedMs = 10,
                ResourcesMonitored = new List<string> { "Dispatcher" },
                StillBusy = new List<string>(),
            });

        var json = await this.tool.PumpUntilIdleAsync();

        Assert.That(json, Does.Contain("\"idleReached\""));
        Assert.That(json, Does.Contain("\"elapsedMs\""));
        Assert.That(json, Does.Contain("\"resourcesMonitored\""));
        Assert.That(json, Does.Contain("\"stillBusy\""));
        Assert.That(json, Does.Not.Contain("\"IdleReached\""));
        Assert.That(json, Does.Not.Contain("\"ElapsedMs\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnPumpUntilIdle = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.PumpUntilIdleAsync());

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }

    // ── Error path: DispatcherBusy ─────────────────────────────────────────────

    [Test]
    public void DispatcherBusy_ThrowsMcpException()
    {
        this.fake.OnPumpUntilIdle = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher is busy");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.PumpUntilIdleAsync(timeoutMs: 5000));

        Assert.That(ex!.Message, Does.Contain("DISPATCHER_BUSY"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
