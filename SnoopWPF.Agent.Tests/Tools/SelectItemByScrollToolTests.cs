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
/// Unit tests for <see cref="SelectItemByScrollTool"/>.
/// </summary>
[TestFixture]
public class SelectItemByScrollToolTests
{
    private FakeSnoopInspector fake = null!;

    private SelectItemByScrollTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SelectItemByScrollTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSelectItemByScroll = (nodeId, targetIndex, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "Item A",
                NewValue = "Item B",
            });

        var json = await this.tool.SelectItemByScrollAsync("0:5", 1);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("Item B"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        int capturedIndex = -1;

        this.fake.OnSelectItemByScroll = (nodeId, targetIndex, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIndex = targetIndex;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SelectItemByScrollAsync("0:8", 500);

        Assert.That(capturedNodeId, Is.EqualTo("0:8"));
        Assert.That(capturedIndex, Is.EqualTo(500));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByScrollAsync("0:99", 0));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByScrollAsync("0:1", 0));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Error path: InvalidArgument (out-of-range index) ───────────────────────

    [Test]
    public void InvalidArgument_ThrowsMcpException()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.InvalidArgument, "targetIndex 9999 is out of range");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByScrollAsync("0:1", 9999));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSelectItemByScroll = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SelectItemByScrollAsync("0:1", 0, ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.SelectItemByScrollAsync("0:1", 0);

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemByScrollAsync("0:1", 0));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
