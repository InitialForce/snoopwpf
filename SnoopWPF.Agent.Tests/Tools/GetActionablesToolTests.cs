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
/// Unit tests for <see cref="GetActionablesTool"/>.
/// </summary>
[TestFixture]
public class GetActionablesToolTests
{
    private FakeSnoopInspector fake = null!;
    private GetActionablesTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetActionablesTool(this.fake);
    }

    [Test]
    public async Task HappyPath_ReturnsActionablesList()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            Task.FromResult(new ActionablesResultDto
            {
                Items = new List<ActionableDto>
                {
                    new()
                    {
                        NodeId = "0:5",
                        Kind = "button",
                        Label = "Save",
                        Name = "SaveButton",
                        AutomationId = "SaveButton",
                        TypeName = "Button",
                        IsEnabled = true,
                        HasCommandBinding = true,
                    },
                    new()
                    {
                        NodeId = "0:7",
                        Kind = "input",
                        Label = "Username",
                        Name = "UsernameBox",
                        AutomationId = "UsernameBox",
                        TypeName = "TextBox",
                        IsEnabled = true,
                        HasCommandBinding = false,
                    },
                },
                TotalScanned = 42,
                Truncated = false,
            });

        var json = await this.tool.GetActionablesAsync();

        var doc = JsonNode.Parse(json)!.AsObject();
        var items = doc["items"]!.AsArray();
        Assert.That(items.Count, Is.EqualTo(2));

        var first = items[0]!;
        Assert.That(first["nodeId"]!.GetValue<string>(), Is.EqualTo("0:5"));
        Assert.That(first["kind"]!.GetValue<string>(), Is.EqualTo("button"));
        Assert.That(first["label"]!.GetValue<string>(), Is.EqualTo("Save"));
        Assert.That(first["automationId"]!.GetValue<string>(), Is.EqualTo("SaveButton"));
        Assert.That(first["isEnabled"]!.GetValue<bool>(), Is.True);
        Assert.That(first["hasCommandBinding"]!.GetValue<bool>(), Is.True);

        Assert.That(doc["totalScanned"]!.GetValue<int>(), Is.EqualTo(42));
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task HappyPath_EmptyResult_WhenNoActionables()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            Task.FromResult(new ActionablesResultDto());

        var json = await this.tool.GetActionablesAsync();

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["items"]!.AsArray().Count, Is.EqualTo(0));
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedRoot = "sentinel";
        int capturedMax = -1;

        this.fake.OnGetActionables = (rootNodeId, maxResults, _) =>
        {
            capturedRoot = rootNodeId;
            capturedMax = maxResults;
            return Task.FromResult(new ActionablesResultDto());
        };

        await this.tool.GetActionablesAsync(rootNodeId: "0:42", maxResults: 25);

        Assert.That(capturedRoot, Is.EqualTo("0:42"));
        Assert.That(capturedMax, Is.EqualTo(25));
    }

    [Test]
    public async Task DefaultParameters_ForwardNullRootAnd100Max()
    {
        string? capturedRoot = "sentinel";
        int capturedMax = -1;

        this.fake.OnGetActionables = (rootNodeId, maxResults, _) =>
        {
            capturedRoot = rootNodeId;
            capturedMax = maxResults;
            return Task.FromResult(new ActionablesResultDto());
        };

        await this.tool.GetActionablesAsync();

        Assert.That(capturedRoot, Is.Null);
        Assert.That(capturedMax, Is.EqualTo(100));
    }

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            Task.FromResult(new ActionablesResultDto
            {
                Items = new List<ActionableDto>
                {
                    new() { NodeId = "0:1", Kind = "button", IsEnabled = true },
                },
            });

        var json = await this.tool.GetActionablesAsync();

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"kind\""));
        Assert.That(json, Does.Contain("\"isEnabled\""));
        Assert.That(json, Does.Contain("\"hasCommandBinding\""));
        Assert.That(json, Does.Contain("\"automationId\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"IsEnabled\""));
    }

    [Test]
    public async Task TruncatedFlag_RoundTripsWhenSet()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            Task.FromResult(new ActionablesResultDto
            {
                Items = new List<ActionableDto>(),
                Truncated = true,
                TotalScanned = 200,
            });

        var json = await this.tool.GetActionablesAsync();
        var doc = JsonNode.Parse(json)!.AsObject();

        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["totalScanned"]!.GetValue<int>(), Is.EqualTo(200));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetActionablesAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetActionables = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetActionablesAsync(rootNodeId: "0:bogus"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
    }
}
