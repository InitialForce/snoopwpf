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
/// Unit tests for <see cref="ExpandCollapseTool"/>.
/// </summary>
[TestFixture]
public class ExpandCollapseToolTests
{
    private FakeSnoopInspector fake = null!;

    private ExpandCollapseTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new ExpandCollapseTool(this.fake);
    }

    // ── Happy path: expand ──────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_Expand_ReturnsStateDelta()
    {
        this.fake.OnExpandCollapse = (nodeId, action, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
            });

        var json = await this.tool.ExpandCollapseAsync("0:3", "expand");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
    }

    // ── Happy path: collapse ────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_Collapse_ReturnsStateDelta()
    {
        this.fake.OnExpandCollapse = (nodeId, action, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
            });

        var json = await this.tool.ExpandCollapseAsync("0:3", "collapse");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedAction = null;

        this.fake.OnExpandCollapse = (nodeId, action, ct) =>
        {
            capturedNodeId = nodeId;
            capturedAction = action;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.ExpandCollapseAsync("0:9", "expand");

        Assert.That(capturedNodeId, Is.EqualTo("0:9"));
        Assert.That(capturedAction, Is.EqualTo("expand"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnExpandCollapse = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ExpandCollapseAsync("0:99", "expand"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnExpandCollapse = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.ExpandCollapseAsync("0:1", "expand", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnExpandCollapse = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.ExpandCollapseAsync("0:1", "expand");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnExpandCollapse = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ExpandCollapseAsync("0:1", "expand"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
