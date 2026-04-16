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
/// BEAD-008: Tests for <see cref="InspectElementTool"/>.
/// </summary>
[TestFixture]
public class InspectElementToolTests
{
    private FakeSnoopInspector fake = null!;

    private InspectElementTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new InspectElementTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsAllSummaryFields()
    {
        this.fake.OnInspectElement = (nodeId, ct) =>
            Task.FromResult(new InspectElementDto
            {
                NodeId = "0:42",
                TypeName = "Button",
                Name = "OkButton",
                DisplayName = "Button: OkButton",
                Path = new List<string> { "Window", "Grid", "Button" },
                ParentNodeId = "0:10",
                ChildCount = 1,
                Depth = 2,
                DispatcherId = 0,
                IsVisible = true,
                ActualWidth = 80.0,
                ActualHeight = 30.0,
                DataContextType = "MyViewModel",
                HasBindingErrors = false,
                BindingErrorCount = 0,
                TriggerCount = null,
                BehaviorCount = null,
            });

        var json = await this.tool.InspectElementAsync("0:42");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["nodeId"]!.GetValue<string>(), Is.EqualTo("0:42"));
        Assert.That(doc["typeName"]!.GetValue<string>(), Is.EqualTo("Button"));
        Assert.That(doc["name"]!.GetValue<string>(), Is.EqualTo("OkButton"));
        Assert.That(doc["isVisible"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["actualWidth"]!.GetValue<double>(), Is.EqualTo(80.0));
        Assert.That(doc["actualHeight"]!.GetValue<double>(), Is.EqualTo(30.0));
        Assert.That(doc["dataContextType"]!.GetValue<string>(), Is.EqualTo("MyViewModel"));
        Assert.That(doc["hasBindingErrors"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["bindingErrorCount"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public async Task HappyPath_TriggerCountAndBehaviorCount_AreNullByDefault()
    {
        this.fake.OnInspectElement = (_, _) =>
            Task.FromResult(new InspectElementDto
            {
                NodeId = "0:1",
                TriggerCount = null,
                BehaviorCount = null,
            });

        var json = await this.tool.InspectElementAsync("0:1");

        var doc = JsonNode.Parse(json)!;
        // null properties serialize as JSON null
        Assert.That(doc["triggerCount"], Is.Null.Or.EqualTo(JsonNode.Parse("null")));
        Assert.That(doc["behaviorCount"], Is.Null.Or.EqualTo(JsonNode.Parse("null")));
    }

    [Test]
    public async Task HappyPath_PathArrayIncluded()
    {
        this.fake.OnInspectElement = (_, _) =>
            Task.FromResult(new InspectElementDto
            {
                NodeId = "0:5",
                Path = new List<string> { "Window", "Grid", "StackPanel", "TextBlock" },
            });

        var json = await this.tool.InspectElementAsync("0:5");

        var doc = JsonNode.Parse(json)!;
        var path = doc["path"]!.AsArray();
        Assert.That(path.Count, Is.EqualTo(4));
        Assert.That(path[0]!.GetValue<string>(), Is.EqualTo("Window"));
        Assert.That(path[3]!.GetValue<string>(), Is.EqualTo("TextBlock"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = null;

        this.fake.OnInspectElement = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return Task.FromResult(new InspectElementDto { NodeId = nodeId });
        };

        await this.tool.InspectElementAsync("0:77");

        Assert.That(capturedNodeId, Is.EqualTo("0:77"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnInspectElement = (_, _) =>
            Task.FromResult(new InspectElementDto
            {
                NodeId = "0:1",
                HasBindingErrors = true,
                BindingErrorCount = 3,
            });

        var json = await this.tool.InspectElementAsync("0:1");

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"hasBindingErrors\""));
        Assert.That(json, Does.Contain("\"bindingErrorCount\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"HasBindingErrors\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException_WithSuggestion()
    {
        this.fake.OnInspectElement = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node 0:99 was garbage collected");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.InspectElementAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
        Assert.That(ex.Message, Does.Contain("Re-navigate from wpf_get_windows"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnInspectElement = (_, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out waiting for dispatcher");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.InspectElementAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
    }
}
