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
/// Unit tests for <see cref="GetListItemsTool"/>.
/// </summary>
[TestFixture]
public class GetListItemsToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetListItemsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetListItemsTool(this.fake);
    }

    // ── Happy path: 3 items, second selected ────────────────────────────────────

    [Test]
    public async Task HappyPath_ThreeItems_SecondSelected()
    {
        this.fake.OnGetListItems = (nodeId, ct) =>
            Task.FromResult(new List<ListItemDto>
            {
                new() { Index = 0, NodeId = "0:10", DisplayName = "Alpha", IsSelected = false },
                new() { Index = 1, NodeId = "0:11", DisplayName = "Beta",  IsSelected = true },
                new() { Index = 2, NodeId = "0:12", DisplayName = "Gamma", IsSelected = false },
            });

        var json = await this.tool.GetListItemsAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(3));

        Assert.That(doc[0]!["index"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(doc[0]!["isSelected"]!.GetValue<bool>(), Is.False);
        Assert.That(doc[0]!["displayName"]!.GetValue<string>(), Is.EqualTo("Alpha"));

        Assert.That(doc[1]!["index"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(doc[1]!["isSelected"]!.GetValue<bool>(), Is.True);
        Assert.That(doc[1]!["displayName"]!.GetValue<string>(), Is.EqualTo("Beta"));

        Assert.That(doc[2]!["index"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(doc[2]!["isSelected"]!.GetValue<bool>(), Is.False);
    }

    // ── Empty list ──────────────────────────────────────────────────────────────

    [Test]
    public async Task EmptyList_ReturnsEmptyArray()
    {
        this.fake.OnGetListItems = (nodeId, ct) =>
            Task.FromResult(new List<ListItemDto>());

        var json = await this.tool.GetListItemsAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(0));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = null;

        this.fake.OnGetListItems = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return Task.FromResult(new List<ListItemDto>());
        };

        await this.tool.GetListItemsAsync("0:42");

        Assert.That(capturedNodeId, Is.EqualTo("0:42"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetListItems = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetListItemsAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
    }

    // ── Error path: InvalidArgument (not an ItemsControl) ──────────────────────

    [Test]
    public void InvalidArgument_ThrowsMcpException()
    {
        this.fake.OnGetListItems = (_, _) =>
            throw new SnoopException(SnoopErrorCode.InvalidArgument, "not an ItemsControl");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetListItemsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnGetListItems = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new List<ListItemDto>());
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.GetListItemsAsync("0:1", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetListItems = (_, _) =>
            Task.FromResult(new List<ListItemDto>
            {
                new() { Index = 0, NodeId = "0:10", DisplayName = "Item", IsSelected = true },
            });

        var json = await this.tool.GetListItemsAsync("0:1");

        Assert.That(json, Does.Contain("\"index\""));
        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"displayName\""));
        Assert.That(json, Does.Contain("\"isSelected\""));
        Assert.That(json, Does.Not.Contain("\"Index\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"DisplayName\""));
        Assert.That(json, Does.Not.Contain("\"IsSelected\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnGetListItems = (_, _) =>
            throw new ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetListItemsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
