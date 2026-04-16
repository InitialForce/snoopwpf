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
/// Unit tests for <see cref="GetTriggersTool"/>.
/// </summary>
[TestFixture]
public class GetTriggersToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetTriggersTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetTriggersTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsTriggerList()
    {
        this.fake.OnGetTriggers = (nodeId, ct) =>
            System.Threading.Tasks.Task.FromResult(new List<TriggerDto>
            {
                new TriggerDto
                {
                    TriggerType = "Trigger",
                    IsActive = true,
                    Source = "Style",
                    Conditions = new List<TriggerConditionDto>
                    {
                        new TriggerConditionDto { Property = "IsMouseOver", Value = "True" },
                    },
                    Setters = new List<TriggerSetterDto>
                    {
                        new TriggerSetterDto { Property = "Background", Value = "Blue" },
                    },
                },
            });

        var json = await this.tool.GetTriggersAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(1));

        var trigger = doc[0]!;
        Assert.That(trigger["triggerType"]!.GetValue<string>(), Is.EqualTo("Trigger"));
        Assert.That(trigger["isActive"]!.GetValue<bool>(), Is.True);
        Assert.That(trigger["source"]!.GetValue<string>(), Is.EqualTo("Style"));

        var conditions = trigger["conditions"]!.AsArray();
        Assert.That(conditions.Count, Is.EqualTo(1));
        Assert.That(conditions[0]!["property"]!.GetValue<string>(), Is.EqualTo("IsMouseOver"));
        Assert.That(conditions[0]!["value"]!.GetValue<string>(), Is.EqualTo("True"));

        var setters = trigger["setters"]!.AsArray();
        Assert.That(setters.Count, Is.EqualTo(1));
        Assert.That(setters[0]!["property"]!.GetValue<string>(), Is.EqualTo("Background"));
        Assert.That(setters[0]!["value"]!.GetValue<string>(), Is.EqualTo("Blue"));
    }

    [Test]
    public async Task HappyPath_EmptyTriggers_WhenNoneDefined()
    {
        this.fake.OnGetTriggers = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<TriggerDto>());

        var json = await this.tool.GetTriggersAsync("0:5");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task HappyPath_DataTrigger_FromControlTemplate()
    {
        this.fake.OnGetTriggers = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<TriggerDto>
            {
                new TriggerDto
                {
                    TriggerType = "DataTrigger",
                    IsActive = false,
                    Source = "ControlTemplate",
                    Conditions = new List<TriggerConditionDto>
                    {
                        new TriggerConditionDto { Property = "IsEnabled", Value = "False" },
                    },
                    Setters = new List<TriggerSetterDto>
                    {
                        new TriggerSetterDto { Property = "Opacity", Value = "0.5" },
                    },
                },
                new TriggerDto
                {
                    TriggerType = "EventTrigger",
                    IsActive = false,
                    Source = "Element",
                    Conditions = new List<TriggerConditionDto>(),
                    Setters = new List<TriggerSetterDto>(),
                },
            });

        var json = await this.tool.GetTriggersAsync("0:7");

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(2));
        Assert.That(doc[0]!["triggerType"]!.GetValue<string>(), Is.EqualTo("DataTrigger"));
        Assert.That(doc[0]!["source"]!.GetValue<string>(), Is.EqualTo("ControlTemplate"));
        Assert.That(doc[1]!["triggerType"]!.GetValue<string>(), Is.EqualTo("EventTrigger"));
        Assert.That(doc[1]!["source"]!.GetValue<string>(), Is.EqualTo("Element"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeId_ToInspector()
    {
        string? capturedNodeId = null;

        this.fake.OnGetTriggers = (nodeId, ct) =>
        {
            capturedNodeId = nodeId;
            return System.Threading.Tasks.Task.FromResult(new List<TriggerDto>());
        };

        await this.tool.GetTriggersAsync("0:99");

        Assert.That(capturedNodeId, Is.EqualTo("0:99"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetTriggers = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<TriggerDto>
            {
                new TriggerDto
                {
                    TriggerType = "Trigger",
                    IsActive = true,
                    Source = "Style",
                    Conditions = new List<TriggerConditionDto>(),
                    Setters = new List<TriggerSetterDto>(),
                },
            });

        var json = await this.tool.GetTriggersAsync("0:1");

        Assert.That(json, Does.Contain("\"triggerType\""));
        Assert.That(json, Does.Contain("\"isActive\""));
        Assert.That(json, Does.Contain("\"conditions\""));
        Assert.That(json, Does.Contain("\"setters\""));
        Assert.That(json, Does.Not.Contain("\"TriggerType\""));
        Assert.That(json, Does.Not.Contain("\"IsActive\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetTriggers = (_, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetTriggersAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetTriggers = (_, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetTriggersAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnGetTriggers = (_, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetTriggersAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
