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
/// Unit tests for <see cref="GetAncestorsTool"/>.
/// </summary>
[TestFixture]
public class GetAncestorsToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetAncestorsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetAncestorsTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsAncestorChain()
    {
        this.fake.OnGetAncestors = (nodeId, maxLevels, ct) =>
            System.Threading.Tasks.Task.FromResult(new List<AncestorDto>
            {
                new AncestorDto
                {
                    NodeId = "0:3",
                    TypeName = "StackPanel",
                    Name = string.Empty,
                    DataContextType = "MainViewModel",
                },
                new AncestorDto
                {
                    NodeId = "0:1",
                    TypeName = "Window",
                    Name = "MainWindow",
                    DataContextType = "MainViewModel",
                },
            });

        var json = await this.tool.GetAncestorsAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(2));

        var parent = doc[0]!;
        Assert.That(parent["nodeId"]!.GetValue<string>(), Is.EqualTo("0:3"));
        Assert.That(parent["typeName"]!.GetValue<string>(), Is.EqualTo("StackPanel"));
        Assert.That(parent["dataContextType"]!.GetValue<string>(), Is.EqualTo("MainViewModel"));

        var root = doc[1]!;
        Assert.That(root["nodeId"]!.GetValue<string>(), Is.EqualTo("0:1"));
        Assert.That(root["name"]!.GetValue<string>(), Is.EqualTo("MainWindow"));
    }

    [Test]
    public async Task HappyPath_EmptyAncestors_WhenAtRoot()
    {
        this.fake.OnGetAncestors = (_, _, _) =>
            System.Threading.Tasks.Task.FromResult(new List<AncestorDto>());

        var json = await this.tool.GetAncestorsAsync("0:1");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(0));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_AndMaxLevels()
    {
        string? capturedNodeId = null;
        int? capturedMaxLevels = -1;

        this.fake.OnGetAncestors = (nodeId, maxLevels, ct) =>
        {
            capturedNodeId = nodeId;
            capturedMaxLevels = maxLevels;
            return System.Threading.Tasks.Task.FromResult(new List<AncestorDto>());
        };

        await this.tool.GetAncestorsAsync("0:10", maxLevels: 3);

        Assert.That(capturedNodeId, Is.EqualTo("0:10"));
        Assert.That(capturedMaxLevels, Is.EqualTo(3));
    }

    [Test]
    public async Task DefaultMaxLevels_IsNull()
    {
        int? capturedMaxLevels = -1;

        this.fake.OnGetAncestors = (nodeId, maxLevels, ct) =>
        {
            capturedMaxLevels = maxLevels;
            return System.Threading.Tasks.Task.FromResult(new List<AncestorDto>());
        };

        await this.tool.GetAncestorsAsync("0:5");

        Assert.That(capturedMaxLevels, Is.Null);
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetAncestors = (_, _, _) =>
            System.Threading.Tasks.Task.FromResult(new List<AncestorDto>
            {
                new AncestorDto { NodeId = "0:1", TypeName = "Window", DataContextType = "Vm" },
            });

        var json = await this.tool.GetAncestorsAsync("0:5");

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"typeName\""));
        Assert.That(json, Does.Contain("\"dataContextType\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"TypeName\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetAncestors = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetAncestorsAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetAncestors = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetAncestorsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnGetAncestors = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetAncestorsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
