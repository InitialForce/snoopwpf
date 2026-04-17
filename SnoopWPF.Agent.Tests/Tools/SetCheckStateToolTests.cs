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
/// Unit tests for <see cref="SetCheckStateTool"/>.
/// </summary>
[TestFixture]
public class SetCheckStateToolTests
{
    private FakeSnoopInspector fake = null!;

    private SetCheckStateTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SetCheckStateTool(this.fake);
    }

    // ── Happy path: checked ─────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_Checked_ReturnsStateDelta()
    {
        this.fake.OnSetCheckState = (nodeId, state, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "false",
                NewValue = "true",
            });

        var json = await this.tool.SetCheckStateAsync("0:3", "checked");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("true"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedState = null;

        this.fake.OnSetCheckState = (nodeId, state, ct) =>
        {
            capturedNodeId = nodeId;
            capturedState = state;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SetCheckStateAsync("0:4", "unchecked");

        Assert.That(capturedNodeId, Is.EqualTo("0:4"));
        Assert.That(capturedState, Is.EqualTo("unchecked"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSetCheckState = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetCheckStateAsync("0:99", "checked"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSetCheckState = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetCheckStateAsync("0:1", "checked"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSetCheckState = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SetCheckStateAsync("0:1", "checked", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSetCheckState = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "false",
                NewValue = "true",
            });

        var json = await this.tool.SetCheckStateAsync("0:1", "checked");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Contain("\"previousValue\""));
        Assert.That(json, Does.Contain("\"newValue\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnSetCheckState = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetCheckStateAsync("0:1", "checked"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
