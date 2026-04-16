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
/// Unit tests for <see cref="GetBehaviorsTool"/>.
/// </summary>
[TestFixture]
public class GetBehaviorsToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetBehaviorsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetBehaviorsTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsBehaviorList()
    {
        this.fake.OnGetBehaviors = (nodeId, ct) =>
            System.Threading.Tasks.Task.FromResult(new List<BehaviorDto>
            {
                new BehaviorDto
                {
                    TypeName = "EventToCommandBehavior",
                    AssemblyName = "MyApp.Behaviors",
                    Properties = new List<NameValuePairDto>
                    {
                        new NameValuePairDto { Name = "EventName", Value = "Click" },
                        new NameValuePairDto { Name = "Command", Value = "SaveCommand" },
                    },
                },
            });

        var json = await this.tool.GetBehaviorsAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(1));

        var behavior = doc[0]!;
        Assert.That(behavior["typeName"]!.GetValue<string>(), Is.EqualTo("EventToCommandBehavior"));
        Assert.That(behavior["assemblyName"]!.GetValue<string>(), Is.EqualTo("MyApp.Behaviors"));

        var props = behavior["properties"]!.AsArray();
        Assert.That(props.Count, Is.EqualTo(2));
        Assert.That(props[0]!["name"]!.GetValue<string>(), Is.EqualTo("EventName"));
        Assert.That(props[0]!["value"]!.GetValue<string>(), Is.EqualTo("Click"));
    }

    [Test]
    public async Task HappyPath_EmptyBehaviors_WhenNoneAttached()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<BehaviorDto>());

        var json = await this.tool.GetBehaviorsAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task HappyPath_MultipleBehaviors()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<BehaviorDto>
            {
                new BehaviorDto { TypeName = "FadeBehavior", AssemblyName = "Blend.Behaviors" },
                new BehaviorDto { TypeName = "DragBehavior", AssemblyName = "Blend.Behaviors" },
            });

        var json = await this.tool.GetBehaviorsAsync("0:3");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(2));
        Assert.That(doc[0]!["typeName"]!.GetValue<string>(), Is.EqualTo("FadeBehavior"));
        Assert.That(doc[1]!["typeName"]!.GetValue<string>(), Is.EqualTo("DragBehavior"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = null;

        this.fake.OnGetBehaviors = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return System.Threading.Tasks.Task.FromResult(new List<BehaviorDto>());
        };

        await this.tool.GetBehaviorsAsync("0:42");

        Assert.That(capturedNodeId, Is.EqualTo("0:42"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<BehaviorDto>
            {
                new BehaviorDto
                {
                    TypeName = "SomeBehavior",
                    AssemblyName = "MyLib",
                    Properties = new List<NameValuePairDto>(),
                },
            });

        var json = await this.tool.GetBehaviorsAsync("0:1");

        Assert.That(json, Does.Contain("\"typeName\""));
        Assert.That(json, Does.Contain("\"assemblyName\""));
        Assert.That(json, Does.Contain("\"properties\""));
        Assert.That(json, Does.Not.Contain("\"TypeName\""));
        Assert.That(json, Does.Not.Contain("\"AssemblyName\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetBehaviorsAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetBehaviorsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnGetBehaviors = (_, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetBehaviorsAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
    }
}
