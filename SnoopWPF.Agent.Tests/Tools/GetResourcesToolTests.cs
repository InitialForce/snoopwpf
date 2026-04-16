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
/// Unit tests for <see cref="GetResourcesTool"/>.
/// </summary>
[TestFixture]
public class GetResourcesToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetResourcesTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetResourcesTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsResourcePage()
    {
        this.fake.OnGetResources = (nodeId, resourceKey, cursor, take, ct) =>
            System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto>
            {
                Items = new List<ResourceDto>
                {
                    new ResourceDto
                    {
                        Key = "PrimaryBrush",
                        ValueTypeName = "SolidColorBrush",
                        ValueSummary = "#FF0000",
                        Origin = "Application",
                        DictionarySource = "App.xaml",
                    },
                    new ResourceDto
                    {
                        Key = "ButtonStyle",
                        ValueTypeName = "Style",
                        ValueSummary = "Style for Button",
                        Origin = "Window",
                        DictionarySource = "MainWindow.xaml",
                    },
                },
                TotalCount = 2,
                HasMore = false,
                NextCursor = null,
            });

        var json = await this.tool.GetResourcesAsync();

        var doc = JsonNode.Parse(json)!;
        var items = doc["items"]!.AsArray();
        Assert.That(items.Count, Is.EqualTo(2));

        var first = items[0]!;
        Assert.That(first["key"]!.GetValue<string>(), Is.EqualTo("PrimaryBrush"));
        Assert.That(first["valueTypeName"]!.GetValue<string>(), Is.EqualTo("SolidColorBrush"));
        Assert.That(first["valueSummary"]!.GetValue<string>(), Is.EqualTo("#FF0000"));
        Assert.That(first["origin"]!.GetValue<string>(), Is.EqualTo("Application"));
        Assert.That(first["dictionarySource"]!.GetValue<string>(), Is.EqualTo("App.xaml"));

        Assert.That(doc["hasMore"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["totalCount"]!.GetValue<int>(), Is.EqualTo(2));
    }

    [Test]
    public async Task HappyPath_CursorPagination_HasMoreAndNextCursor()
    {
        this.fake.OnGetResources = (_, _, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto>
            {
                Items = new List<ResourceDto>
                {
                    new ResourceDto { Key = "Resource1", ValueTypeName = "Style" },
                },
                TotalCount = 100,
                HasMore = true,
                NextCursor = "cursor-page-2",
            });

        var json = await this.tool.GetResourcesAsync(take: 1);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasMore"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["nextCursor"]!.GetValue<string>(), Is.EqualTo("cursor-page-2"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsAllParameters_ToInspector()
    {
        string? capturedNodeId = "not-set";
        string? capturedResourceKey = "not-set";
        string? capturedCursor = "not-set";
        int capturedTake = -1;

        this.fake.OnGetResources = (nodeId, resourceKey, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedResourceKey = resourceKey;
            capturedCursor = cursor;
            capturedTake = take;
            return System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto> { Items = new List<ResourceDto>() });
        };

        await this.tool.GetResourcesAsync(
            nodeId: "0:3",
            resourceKey: "Primary",
            cursor: "page2",
            take: 25);

        Assert.That(capturedNodeId, Is.EqualTo("0:3"));
        Assert.That(capturedResourceKey, Is.EqualTo("Primary"));
        Assert.That(capturedCursor, Is.EqualTo("page2"));
        Assert.That(capturedTake, Is.EqualTo(25));
    }

    [Test]
    public async Task DefaultParameters_NullNodeIdAndKey_DefaultTake()
    {
        string? capturedNodeId = "not-null";
        string? capturedResourceKey = "not-null";
        string? capturedCursor = "not-null";
        int capturedTake = -1;

        this.fake.OnGetResources = (nodeId, resourceKey, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedResourceKey = resourceKey;
            capturedCursor = cursor;
            capturedTake = take;
            return System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto> { Items = new List<ResourceDto>() });
        };

        await this.tool.GetResourcesAsync();

        Assert.That(capturedNodeId, Is.Null);
        Assert.That(capturedResourceKey, Is.Null);
        Assert.That(capturedCursor, Is.Null);
        Assert.That(capturedTake, Is.EqualTo(50));
    }

    // ── ResourceKey filter ────────────────────────────────────────────────────────

    [Test]
    public async Task ResourceKeyFilter_ForwardedToInspector()
    {
        string? capturedResourceKey = null;

        this.fake.OnGetResources = (_, resourceKey, _, _, _) =>
        {
            capturedResourceKey = resourceKey;
            return System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto> { Items = new List<ResourceDto>() });
        };

        await this.tool.GetResourcesAsync(resourceKey: "Button");

        Assert.That(capturedResourceKey, Is.EqualTo("Button"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetResources = (_, _, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new CursorPage<ResourceDto>
            {
                Items = new List<ResourceDto>
                {
                    new ResourceDto { Key = "K", ValueTypeName = "Style", Origin = "Application" },
                },
                HasMore = true,
                TotalCount = 1,
            });

        var json = await this.tool.GetResourcesAsync();

        Assert.That(json, Does.Contain("\"valueTypeName\""));
        Assert.That(json, Does.Contain("\"valueSummary\""));
        Assert.That(json, Does.Contain("\"dictionarySource\""));
        Assert.That(json, Does.Contain("\"hasMore\""));
        Assert.That(json, Does.Not.Contain("\"ValueTypeName\""));
        Assert.That(json, Does.Not.Contain("\"HasMore\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetResources = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetResourcesAsync(nodeId: "0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetResources = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetResourcesAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }
}
