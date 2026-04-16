namespace SnoopWPF.Agent.Tests.Tools;

using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// BEAD-007: Tests for <see cref="GetChildrenTool"/> — cursor-paginated children.
/// </summary>
[TestFixture]
public class GetChildrenToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetChildrenTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetChildrenTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsPageWithItems()
    {
        this.fake.OnGetChildren = (nodeId, treeType, cursor, take, ct) =>
            Task.FromResult(new CursorPage<NodeDto>
            {
                Items = new List<NodeDto>
                {
                    new NodeDto { NodeId = "0:2", TypeName = "Grid", DisplayName = "Grid" },
                    new NodeDto { NodeId = "0:3", TypeName = "StackPanel", DisplayName = "StackPanel" },
                },
                TotalCount = 2,
                HasMore = false,
                NextCursor = null,
                Stale = false,
            });

        var json = await this.tool.GetChildrenAsync(nodeId: "0:1");

        var doc = JsonNode.Parse(json)!;
        var items = doc["items"]!.AsArray();
        Assert.That(items.Count, Is.EqualTo(2));
        Assert.That(items[0]!["nodeId"]!.GetValue<string>(), Is.EqualTo("0:2"));
        Assert.That(doc["hasMore"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["stale"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task HappyPath_CursorPagination_ReturnsNextCursor()
    {
        this.fake.OnGetChildren = (_, _, _, _, _) =>
            Task.FromResult(new CursorPage<NodeDto>
            {
                Items = new List<NodeDto> { new NodeDto { NodeId = "0:10" } },
                TotalCount = 50,
                HasMore = true,
                NextCursor = "cursor-abc123",
                Stale = false,
            });

        var json = await this.tool.GetChildrenAsync(nodeId: "0:1", take: 1);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasMore"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["nextCursor"]!.GetValue<string>(), Is.EqualTo("cursor-abc123"));
        Assert.That(doc["totalCount"]!.GetValue<int>(), Is.EqualTo(50));
    }

    [Test]
    public async Task HappyPath_StaleFlag_PropagatedInResponse()
    {
        this.fake.OnGetChildren = (_, _, _, _, _) =>
            Task.FromResult(new CursorPage<NodeDto>
            {
                Items = new List<NodeDto>(),
                TotalCount = 0,
                HasMore = false,
                Stale = true,
            });

        var json = await this.tool.GetChildrenAsync(cursor: "old-cursor");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["stale"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsAllParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedTreeType = null;
        string? capturedCursor = null;
        int capturedTake = -1;

        this.fake.OnGetChildren = (nodeId, treeType, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedTreeType = treeType;
            capturedCursor = cursor;
            capturedTake = take;
            return Task.FromResult(new CursorPage<NodeDto> { Items = new List<NodeDto>() });
        };

        await this.tool.GetChildrenAsync(
            nodeId: "0:5",
            treeType: "logical",
            cursor: "page2",
            take: 25);

        Assert.That(capturedNodeId, Is.EqualTo("0:5"));
        Assert.That(capturedTreeType, Is.EqualTo("logical"));
        Assert.That(capturedCursor, Is.EqualTo("page2"));
        Assert.That(capturedTake, Is.EqualTo(25));
    }

    [Test]
    public async Task DefaultParameters_NullNodeId_VisualTree_NoCursor_Take50()
    {
        string? capturedNodeId = "not-null";
        string capturedTreeType = string.Empty;
        string? capturedCursor = "not-null";
        int capturedTake = -1;

        this.fake.OnGetChildren = (nodeId, treeType, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedTreeType = treeType;
            capturedCursor = cursor;
            capturedTake = take;
            return Task.FromResult(new CursorPage<NodeDto> { Items = new List<NodeDto>() });
        };

        await this.tool.GetChildrenAsync();

        Assert.That(capturedNodeId, Is.Null);
        Assert.That(capturedTreeType, Is.EqualTo("visual"));
        Assert.That(capturedCursor, Is.Null);
        Assert.That(capturedTake, Is.EqualTo(50));
    }

    // ── Camelcase output ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetChildren = (_, _, _, _, _) =>
            Task.FromResult(new CursorPage<NodeDto>
            {
                Items = new List<NodeDto> { new NodeDto { NodeId = "0:1" } },
                HasMore = false,
                TotalCount = 1,
            });

        var json = await this.tool.GetChildrenAsync();

        Assert.That(json, Does.Contain("\"hasMore\""));
        Assert.That(json, Does.Contain("\"totalCount\""));
        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Not.Contain("\"HasMore\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetChildren = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetChildrenAsync(nodeId: "0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void DispatcherBusy_ThrowsMcpException()
    {
        this.fake.OnGetChildren = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher busy");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetChildrenAsync());

        Assert.That(ex!.Message, Does.Contain("DISPATCHER_BUSY"));
    }
}
