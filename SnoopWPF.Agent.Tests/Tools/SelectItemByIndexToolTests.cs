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
/// Unit tests for <see cref="SelectItemByIndexTool"/>.
/// </summary>
[TestFixture]
public class SelectItemByIndexToolTests
{
    private FakeSnoopInspector fake = null!;

    private SelectItemByIndexTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SelectItemByIndexTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSelectItemByIndex = (nodeId, index, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "0",
                NewValue = "5",
            });

        var json = await this.tool.SelectItemByIndexAsync("0:5", 5);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        int capturedIndex = -1;

        this.fake.OnSelectItemByIndex = (nodeId, index, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIndex = index;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SelectItemByIndexAsync("0:8", 5);

        Assert.That(capturedNodeId, Is.EqualTo("0:8"));
        Assert.That(capturedIndex, Is.EqualTo(5));
    }

    // ── StateUnchanged when item already selected ───────────────────────────────

    [Test]
    public async Task StateUnchanged_WhenAlreadySelected()
    {
        this.fake.OnSelectItemByIndex = (nodeId, index, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = false,
                FailureReason = FailureReason.StateUnchanged,
            });

        var json = await this.tool.SelectItemByIndexAsync("0:1", 5);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.False);
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByIndexAsync("0:99", 0));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByIndexAsync("0:1", 0));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Error path: InvalidArgument ─────────────────────────────────────────────

    [Test]
    public void InvalidArgument_ThrowsMcpException()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.InvalidArgument, "index 9999 is out of range");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByIndexAsync("0:1", 9999));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSelectItemByIndex = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SelectItemByIndexAsync("0:1", 0, ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.SelectItemByIndexAsync("0:1", 0);

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByIndexAsync("0:1", 0));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
