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
/// Unit tests for <see cref="SetTextValueTool"/>.
/// </summary>
[TestFixture]
public class SetTextValueToolTests
{
    private FakeSnoopInspector fake = null!;

    private SetTextValueTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SetTextValueTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSetTextValue = (nodeId, value, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "old text",
                NewValue = "new text",
            });

        var json = await this.tool.SetTextValueAsync("0:6", "new text");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("new text"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedValue = null;

        this.fake.OnSetTextValue = (nodeId, value, ct) =>
        {
            capturedNodeId = nodeId;
            capturedValue = value;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SetTextValueAsync("0:11", "hello world");

        Assert.That(capturedNodeId, Is.EqualTo("0:11"));
        Assert.That(capturedValue, Is.EqualTo("hello world"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSetTextValue = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetTextValueAsync("0:99", "text"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSetTextValue = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetTextValueAsync("0:1", "text"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSetTextValue = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SetTextValueAsync("0:1", "text", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSetTextValue = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "old",
                NewValue = "new",
            });

        var json = await this.tool.SetTextValueAsync("0:1", "new");

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
        this.fake.OnSetTextValue = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetTextValueAsync("0:1", "text"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
