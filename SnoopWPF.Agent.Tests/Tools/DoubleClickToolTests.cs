namespace SnoopWPF.Agent.Tests.Tools;

using System;
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
/// Unit tests for <see cref="DoubleClickTool"/>.
/// </summary>
[TestFixture]
public class DoubleClickToolTests
{
    private FakeSnoopInspector fake = null!;

    private DoubleClickTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new DoubleClickTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnDoubleClick = (nodeId, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
            });

        var json = await this.tool.DoubleClickAsync("0:5");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = null;

        this.fake.OnDoubleClick = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.DoubleClickAsync("0:42");

        Assert.That(capturedNodeId, Is.EqualTo("0:42"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnDoubleClick = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.DoubleClickAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnDoubleClick = (_, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Automation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.DoubleClickAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnDoubleClick = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.DoubleClickAsync("0:1", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnDoubleClick = (_, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.DoubleClickAsync("0:1");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Contain("\"elementVisible\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnDoubleClick = (_, _) =>
            throw new ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.DoubleClickAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
