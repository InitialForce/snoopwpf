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
/// Unit tests for <see cref="SelectItemTool"/>.
/// </summary>
[TestFixture]
public class SelectItemToolTests
{
    private FakeSnoopInspector fake = null!;

    private SelectItemTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SelectItemTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSelectItem = (nodeId, identifier, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
            });

        var json = await this.tool.SelectItemAsync("0:5", "Item A");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedIdentifier = null;

        this.fake.OnSelectItem = (nodeId, identifier, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIdentifier = identifier;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SelectItemAsync("0:8", "2");

        Assert.That(capturedNodeId, Is.EqualTo("0:8"));
        Assert.That(capturedIdentifier, Is.EqualTo("2"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:99", "Item"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", "0"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSelectItem = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SelectItemAsync("0:1", "Item", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.SelectItemAsync("0:1", "0");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", "Item"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
