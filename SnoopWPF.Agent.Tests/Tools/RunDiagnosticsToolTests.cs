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
/// BEAD-011: Tests for <see cref="RunDiagnosticsTool"/>.
/// </summary>
[TestFixture]
public class RunDiagnosticsToolTests
{
    private FakeSnoopInspector fake = null!;

    private RunDiagnosticsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new RunDiagnosticsTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsDiagnosticItems()
    {
        this.fake.OnRunDiagnostics = (nodeId, providers, minLevel, cursor, take, ct) =>
            Task.FromResult(new CursorPage<DiagnosticItemDto>
            {
                Items = new List<DiagnosticItemDto>
                {
                    new DiagnosticItemDto
                    {
                        Name = "BindingLeak",
                        Description = "Binding expression leaked",
                        Area = "Memory",
                        Level = "Warning",
                        NodeId = "0:5",
                        NodePath = new List<string> { "Window", "Grid", "TextBlock" },
                    },
                    new DiagnosticItemDto
                    {
                        Name = "NonVirtualizedList",
                        Description = "ListBox is not virtualizing",
                        Area = "Performance",
                        Level = "Warning",
                        NodeId = "0:8",
                        NodePath = new List<string> { "Window", "Grid", "ListBox" },
                    },
                },
                TotalCount = 2,
                HasMore = false,
            });

        var json = await this.tool.RunDiagnosticsAsync();

        var doc = JsonNode.Parse(json)!;
        var items = doc["items"]!.AsArray();
        Assert.That(items.Count, Is.EqualTo(2));

        var first = items[0]!;
        Assert.That(first["name"]!.GetValue<string>(), Is.EqualTo("BindingLeak"));
        Assert.That(first["level"]!.GetValue<string>(), Is.EqualTo("Warning"));
        Assert.That(first["area"]!.GetValue<string>(), Is.EqualTo("Memory"));
        Assert.That(first["nodeId"]!.GetValue<string>(), Is.EqualTo("0:5"));

        var nodePath = first["nodePath"]!.AsArray();
        Assert.That(nodePath.Count, Is.EqualTo(3));
        Assert.That(nodePath[0]!.GetValue<string>(), Is.EqualTo("Window"));
    }

    [Test]
    public async Task HappyPath_EmptyResults_WhenNoDiagnostics()
    {
        this.fake.OnRunDiagnostics = (_, _, _, _, _, _) =>
            Task.FromResult(new CursorPage<DiagnosticItemDto>
            {
                Items = new List<DiagnosticItemDto>(),
                TotalCount = 0,
                HasMore = false,
            });

        var json = await this.tool.RunDiagnosticsAsync();

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["items"]!.AsArray().Count, Is.EqualTo(0));
        Assert.That(doc["totalCount"]!.GetValue<int>(), Is.EqualTo(0));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsAllParameters()
    {
        string? capturedNodeId = "not-null";
        List<string>? capturedProviders = null;
        string? capturedMinLevel = "not-null";
        string? capturedCursor = "not-null";
        int capturedTake = -1;

        this.fake.OnRunDiagnostics = (nodeId, providers, minLevel, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedProviders = providers;
            capturedMinLevel = minLevel;
            capturedCursor = cursor;
            capturedTake = take;
            return Task.FromResult(new CursorPage<DiagnosticItemDto> { Items = new List<DiagnosticItemDto>() });
        };

        await this.tool.RunDiagnosticsAsync(
            nodeId: "0:5",
            providers: new List<string> { "BindingLeak", "NonVirtualizedLists" },
            minLevel: "Warning",
            cursor: "page2",
            take: 20);

        Assert.That(capturedNodeId, Is.EqualTo("0:5"));
        Assert.That(capturedProviders, Is.EqualTo(new[] { "BindingLeak", "NonVirtualizedLists" }));
        Assert.That(capturedMinLevel, Is.EqualTo("Warning"));
        Assert.That(capturedCursor, Is.EqualTo("page2"));
        Assert.That(capturedTake, Is.EqualTo(20));
    }

    [Test]
    public async Task DefaultParameters_AllNullAndTake100()
    {
        string? capturedNodeId = "not-null";
        List<string>? capturedProviders = new List<string> { "x" };
        string? capturedMinLevel = "not-null";
        string? capturedCursor = "not-null";
        int capturedTake = -1;

        this.fake.OnRunDiagnostics = (nodeId, providers, minLevel, cursor, take, ct) =>
        {
            capturedNodeId = nodeId;
            capturedProviders = providers;
            capturedMinLevel = minLevel;
            capturedCursor = cursor;
            capturedTake = take;
            return Task.FromResult(new CursorPage<DiagnosticItemDto> { Items = new List<DiagnosticItemDto>() });
        };

        await this.tool.RunDiagnosticsAsync();

        Assert.That(capturedNodeId, Is.Null);
        Assert.That(capturedProviders, Is.Null);
        Assert.That(capturedMinLevel, Is.Null);
        Assert.That(capturedCursor, Is.Null);
        Assert.That(capturedTake, Is.EqualTo(100));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnRunDiagnostics = (_, _, _, _, _, _) =>
            Task.FromResult(new CursorPage<DiagnosticItemDto>
            {
                Items = new List<DiagnosticItemDto>
                {
                    new DiagnosticItemDto { Name = "Test", NodeId = "0:1", Level = "Info" },
                },
                TotalCount = 1,
            });

        var json = await this.tool.RunDiagnosticsAsync();

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"totalCount\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"TotalCount\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnRunDiagnostics = (_, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Root node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.RunDiagnosticsAsync(nodeId: "0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnRunDiagnostics = (_, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "No active session");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.RunDiagnosticsAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
