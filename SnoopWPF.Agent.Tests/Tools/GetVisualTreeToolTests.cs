namespace SnoopWPF.Agent.Tests.Tools;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// BEAD-006: Tests for <see cref="GetVisualTreeTool"/>.
/// </summary>
[TestFixture]
public class GetVisualTreeToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetVisualTreeTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetVisualTreeTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsJsonWithRootNode()
    {
        this.fake.OnGetVisualTree = (rootNodeId, maxDepth, treeType, includeProps, ct) =>
            Task.FromResult(new VisualTreeResultDto
            {
                Root = new NodeDto
                {
                    NodeId = "0:1",
                    TypeName = "Window",
                    DisplayName = "MainWindow",
                    ChildCount = 2,
                    Depth = 0,
                },
                Truncated = false,
                ReturnedNodeCount = 1,
            });

        var json = await this.tool.GetVisualTreeAsync();

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["root"]!["nodeId"]!.GetValue<string>(), Is.EqualTo("0:1"));
        Assert.That(doc["root"]!["typeName"]!.GetValue<string>(), Is.EqualTo("Window"));
        Assert.That(doc["returnedNodeCount"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task HappyPath_ForwardsTruncationMetadata()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            Task.FromResult(new VisualTreeResultDto
            {
                Root = new NodeDto { NodeId = "0:1", ChildrenTruncated = true },
                Truncated = true,
                ReturnedNodeCount = 5000,
            });

        var json = await this.tool.GetVisualTreeAsync();

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["returnedNodeCount"]!.GetValue<int>(), Is.EqualTo(5000));
        Assert.That(doc["root"]!["childrenTruncated"]!.GetValue<bool>(), Is.True);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedRootNodeId = null;
        int capturedMaxDepth = 0;
        string? capturedTreeType = null;
        List<string>? capturedIncludeProps = null;

        this.fake.OnGetVisualTree = (rootNodeId, maxDepth, treeType, includeProps, ct) =>
        {
            capturedRootNodeId = rootNodeId;
            capturedMaxDepth = maxDepth;
            capturedTreeType = treeType;
            capturedIncludeProps = includeProps;
            return Task.FromResult(new VisualTreeResultDto { Root = new NodeDto { NodeId = "0:1" } });
        };

        await this.tool.GetVisualTreeAsync(
            rootNodeId: "0:5",
            maxDepth: 7,
            treeType: "logical",
            includeProperties: new List<string> { "Background", "Visibility" });

        Assert.That(capturedRootNodeId, Is.EqualTo("0:5"));
        Assert.That(capturedMaxDepth, Is.EqualTo(7));
        Assert.That(capturedTreeType, Is.EqualTo("logical"));
        Assert.That(capturedIncludeProps, Is.EqualTo(new[] { "Background", "Visibility" }));
    }

    [Test]
    public async Task DefaultParameters_PassNullRootNodeId_AndDefaultDepthAndTreeType()
    {
        string? capturedRootNodeId = "not-null";
        int capturedMaxDepth = -1;
        string? capturedTreeType = null;

        this.fake.OnGetVisualTree = (rootNodeId, maxDepth, treeType, _, _) =>
        {
            capturedRootNodeId = rootNodeId;
            capturedMaxDepth = maxDepth;
            capturedTreeType = treeType;
            return Task.FromResult(new VisualTreeResultDto { Root = new NodeDto { NodeId = "0:1" } });
        };

        await this.tool.GetVisualTreeAsync();

        Assert.That(capturedRootNodeId, Is.Null);
        Assert.That(capturedMaxDepth, Is.EqualTo(3));
        Assert.That(capturedTreeType, Is.EqualTo("visual"));
    }

    // ── IncludeProperties inline values ─────────────────────────────────────────

    [Test]
    public async Task IncludeProperties_AppearsInNodeDto()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            Task.FromResult(new VisualTreeResultDto
            {
                Root = new NodeDto
                {
                    NodeId = "0:1",
                    Properties = new List<NameValuePairDto>
                    {
                        new NameValuePairDto { Name = "Background", Value = "White" },
                        new NameValuePairDto { Name = "Password", Value = "[REDACTED]" },
                    },
                },
                ReturnedNodeCount = 1,
            });

        var json = await this.tool.GetVisualTreeAsync(includeProperties: new List<string> { "Background", "Password" });

        var doc = JsonNode.Parse(json)!;
        var props = doc["root"]!["properties"]!.AsArray();
        Assert.That(props.Count, Is.EqualTo(2));

        var bg = props.First(p => p!["name"]!.GetValue<string>() == "Background");
        Assert.That(bg!["value"]!.GetValue<string>(), Is.EqualTo("White"));

        var pwd = props.First(p => p!["name"]!.GetValue<string>() == "Password");
        Assert.That(pwd!["value"]!.GetValue<string>(), Is.EqualTo("[REDACTED]"));
    }

    // ── JSON camelCase serialisation ─────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            Task.FromResult(new VisualTreeResultDto
            {
                Root = new NodeDto { NodeId = "0:1", HasBindingError = true },
                ReturnedNodeCount = 1,
            });

        var json = await this.tool.GetVisualTreeAsync();

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"hasBindingError\""));
        Assert.That(json, Does.Contain("\"returnedNodeCount\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"HasBindingError\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException_WithCode()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node 0:99 not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetVisualTreeAsync(rootNodeId: "0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException_WithCode()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Application not initialized");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetVisualTreeAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException_WithCode()
    {
        this.fake.OnGetVisualTree = (_, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetVisualTreeAsync());

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
    }
}
