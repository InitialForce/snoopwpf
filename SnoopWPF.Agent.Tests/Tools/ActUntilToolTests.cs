namespace SnoopWPF.Agent.Tests.Tools;

using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="ActUntilTool"/>.
/// </summary>
[TestFixture]
public class ActUntilToolTests
{
    private FakeSnoopInspector fake = null!;
    private ActUntilTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new ActUntilTool(this.fake);
    }

    [Test]
    public async Task HappyPath_ActionFires_PredicateMet()
    {
        this.fake.OnActUntil = (_, _, _, _) =>
            Task.FromResult(new ActUntilResultDto
            {
                ActionResult = new ActionStepResultDto
                {
                    Index = 0,
                    Type = "click",
                    NodeId = "0:5",
                    Success = true,
                },
                Success = true,
                PredicateMet = true,
                ActualValue = "True",
                ElapsedMs = 87,
                PollCount = 3,
            });

        var json = await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "click", NodeId = "0:5" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:9",
                PropertyName = "IsVisible",
                ExpectedValue = "True",
            });

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["predicateMet"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["timedOut"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["actualValue"]!.GetValue<string>(), Is.EqualTo("True"));
        Assert.That(doc["pollCount"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(doc["actionResult"]!["success"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task ActionFails_SkipsPolling()
    {
        this.fake.OnActUntil = (_, _, _, _) =>
            Task.FromResult(new ActUntilResultDto
            {
                ActionResult = new ActionStepResultDto
                {
                    Index = 0,
                    Type = "click",
                    NodeId = "0:5",
                    Success = false,
                    ErrorCode = "ElementNotEnabled",
                    ErrorMessage = "Button is disabled",
                },
                Success = false,
                PredicateMet = false,
                TimedOut = false,
                PollCount = 0,
            });

        var json = await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "click", NodeId = "0:5" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:9",
                PropertyName = "IsVisible",
                ExpectedValue = "True",
            });

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["success"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["predicateMet"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["pollCount"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(doc["actionResult"]!["errorCode"]!.GetValue<string>(), Is.EqualTo("ElementNotEnabled"));
    }

    [Test]
    public async Task PollTimeout_TimedOutTrue_SuccessFalse()
    {
        this.fake.OnActUntil = (_, _, _, _) =>
            Task.FromResult(new ActUntilResultDto
            {
                ActionResult = new ActionStepResultDto
                {
                    Index = 0, Type = "click", NodeId = "0:5", Success = true,
                },
                Success = false,
                PredicateMet = false,
                TimedOut = true,
                ActualValue = "False",
                ElapsedMs = 5000,
                PollCount = 100,
            });

        var json = await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "click", NodeId = "0:5" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:9",
                PropertyName = "IsVisible",
                ExpectedValue = "True",
            });

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["success"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["timedOut"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["actualValue"]!.GetValue<string>(), Is.EqualTo("False"));
    }

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        ActionStepDto? capturedAction = null;
        ActUntilPredicateDto? capturedPredicate = null;
        int capturedTimeout = -1;

        this.fake.OnActUntil = (action, predicate, timeoutMs, _) =>
        {
            capturedAction = action;
            capturedPredicate = predicate;
            capturedTimeout = timeoutMs;
            return Task.FromResult(new ActUntilResultDto
            {
                ActionResult = new ActionStepResultDto { Success = true },
                Success = true,
                PredicateMet = true,
            });
        };

        await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "set_text", NodeId = "0:1", Value = "abc" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:2",
                PropertyName = "Text",
                ExpectedValue = "abc",
                PresenceExpected = "absent",
            },
            timeoutMs: 1234);

        Assert.That(capturedAction!.Type, Is.EqualTo("set_text"));
        Assert.That(capturedAction.NodeId, Is.EqualTo("0:1"));
        Assert.That(capturedAction.Value, Is.EqualTo("abc"));
        Assert.That(capturedPredicate!.TargetNodeId, Is.EqualTo("0:2"));
        Assert.That(capturedPredicate.PropertyName, Is.EqualTo("Text"));
        Assert.That(capturedPredicate.ExpectedValue, Is.EqualTo("abc"));
        Assert.That(capturedPredicate.PresenceExpected, Is.EqualTo("absent"));
        Assert.That(capturedTimeout, Is.EqualTo(1234));
    }

    [Test]
    public async Task DefaultTimeout_Is5000()
    {
        int capturedTimeout = -1;
        this.fake.OnActUntil = (_, _, t, _) =>
        {
            capturedTimeout = t;
            return Task.FromResult(new ActUntilResultDto());
        };

        await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "click", NodeId = "0:1" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:2",
                PropertyName = "IsVisible",
                ExpectedValue = "True",
            });

        Assert.That(capturedTimeout, Is.EqualTo(5000));
    }

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnActUntil = (_, _, _, _) =>
            Task.FromResult(new ActUntilResultDto
            {
                ActionResult = new ActionStepResultDto { Success = true },
                Success = true,
                PredicateMet = true,
            });

        var json = await this.tool.ActUntilAsync(
            action: new ActionStepDto { Type = "click", NodeId = "0:1" },
            predicate: new ActUntilPredicateDto
            {
                TargetNodeId = "0:2",
                PropertyName = "IsVisible",
                ExpectedValue = "True",
            });

        Assert.That(json, Does.Contain("\"actionResult\""));
        Assert.That(json, Does.Contain("\"predicateMet\""));
        Assert.That(json, Does.Contain("\"timedOut\""));
        Assert.That(json, Does.Contain("\"pollCount\""));
        Assert.That(json, Does.Not.Contain("\"PredicateMet\""));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnActUntil = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ActUntilAsync(
                action: new ActionStepDto { Type = "click", NodeId = "0:1" },
                predicate: new ActUntilPredicateDto
                {
                    TargetNodeId = "0:2",
                    PropertyName = "IsVisible",
                    ExpectedValue = "True",
                }));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }
}
