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
/// Unit tests for <see cref="ActSequenceTool"/>.
/// </summary>
[TestFixture]
public class ActSequenceToolTests
{
    private FakeSnoopInspector fake = null!;
    private ActSequenceTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new ActSequenceTool(this.fake);
    }

    [Test]
    public async Task HappyPath_AllStepsSucceed()
    {
        this.fake.OnExecuteActionSequence = (_, _, _) =>
            Task.FromResult(new ActionSequenceResultDto
            {
                AllSucceeded = true,
                StoppedAtIndex = -1,
                Steps = new List<ActionStepResultDto>
                {
                    new() { Index = 0, Type = "click", NodeId = "0:5", Success = true },
                    new() { Index = 1, Type = "set_text", NodeId = "0:7", Success = true },
                    new() { Index = 2, Type = "click", NodeId = "0:9", Success = true },
                },
            });

        var json = await this.tool.ActSequenceAsync(
            steps: new List<ActionStepDto>
            {
                new() { Type = "click", NodeId = "0:5" },
                new() { Type = "set_text", NodeId = "0:7", Value = "hello" },
                new() { Type = "click", NodeId = "0:9" },
            });

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["allSucceeded"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stoppedAtIndex"]!.GetValue<int>(), Is.EqualTo(-1));
        Assert.That(doc["steps"]!.AsArray().Count, Is.EqualTo(3));
    }

    [Test]
    public async Task ForwardsStepsAndStopOnErrorToInspector()
    {
        List<ActionStepDto>? capturedSteps = null;
        bool capturedStop = true;

        this.fake.OnExecuteActionSequence = (steps, stopOnError, _) =>
        {
            capturedSteps = steps;
            capturedStop = stopOnError;
            return Task.FromResult(new ActionSequenceResultDto());
        };

        await this.tool.ActSequenceAsync(
            steps: new List<ActionStepDto>
            {
                new() { Type = "set_text", NodeId = "0:1", Value = "abc" },
            },
            stopOnError: false);

        Assert.That(capturedSteps, Has.Count.EqualTo(1));
        Assert.That(capturedSteps![0].Type, Is.EqualTo("set_text"));
        Assert.That(capturedSteps[0].NodeId, Is.EqualTo("0:1"));
        Assert.That(capturedSteps[0].Value, Is.EqualTo("abc"));
        Assert.That(capturedStop, Is.False);
    }

    [Test]
    public async Task DefaultStopOnError_IsTrue()
    {
        bool capturedStop = false;

        this.fake.OnExecuteActionSequence = (_, stopOnError, _) =>
        {
            capturedStop = stopOnError;
            return Task.FromResult(new ActionSequenceResultDto());
        };

        await this.tool.ActSequenceAsync(steps: new List<ActionStepDto>());

        Assert.That(capturedStop, Is.True);
    }

    [Test]
    public async Task StopOnError_AbortsAtFirstFailure()
    {
        this.fake.OnExecuteActionSequence = (_, _, _) =>
            Task.FromResult(new ActionSequenceResultDto
            {
                AllSucceeded = false,
                StoppedAtIndex = 1,
                Steps = new List<ActionStepResultDto>
                {
                    new() { Index = 0, Type = "click", NodeId = "0:5", Success = true },
                    new()
                    {
                        Index = 1,
                        Type = "set_text",
                        NodeId = "0:7",
                        Success = false,
                        ErrorCode = "ElementNotEnabled",
                        ErrorMessage = "TextBox is read-only",
                    },
                },
            });

        var json = await this.tool.ActSequenceAsync(
            steps: new List<ActionStepDto>
            {
                new() { Type = "click", NodeId = "0:5" },
                new() { Type = "set_text", NodeId = "0:7", Value = "x" },
                new() { Type = "click", NodeId = "0:9" },
            });

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["allSucceeded"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["stoppedAtIndex"]!.GetValue<int>(), Is.EqualTo(1));
        var steps = doc["steps"]!.AsArray();
        Assert.That(steps.Count, Is.EqualTo(2));
        Assert.That(steps[1]!["success"]!.GetValue<bool>(), Is.False);
        Assert.That(steps[1]!["errorCode"]!.GetValue<string>(), Is.EqualTo("ElementNotEnabled"));
    }

    [Test]
    public async Task ContinueOnError_RunsEveryStep()
    {
        this.fake.OnExecuteActionSequence = (_, stopOnError, _) =>
        {
            Assert.That(stopOnError, Is.False);
            return Task.FromResult(new ActionSequenceResultDto
            {
                AllSucceeded = false,
                StoppedAtIndex = 0,
                Steps = new List<ActionStepResultDto>
                {
                    new() { Index = 0, Type = "click", NodeId = "0:5", Success = false, ErrorCode = "ElementNotFound" },
                    new() { Index = 1, Type = "click", NodeId = "0:7", Success = true },
                    new() { Index = 2, Type = "click", NodeId = "0:9", Success = true },
                },
            });
        };

        var json = await this.tool.ActSequenceAsync(
            steps: new List<ActionStepDto>
            {
                new() { Type = "click", NodeId = "0:5" },
                new() { Type = "click", NodeId = "0:7" },
                new() { Type = "click", NodeId = "0:9" },
            },
            stopOnError: false);

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["steps"]!.AsArray().Count, Is.EqualTo(3));
        Assert.That(doc["allSucceeded"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task EmptySequence_ReturnsAllSucceededTrue()
    {
        this.fake.OnExecuteActionSequence = (steps, _, _) =>
        {
            Assert.That(steps, Is.Empty);
            return Task.FromResult(new ActionSequenceResultDto
            {
                AllSucceeded = true,
                StoppedAtIndex = -1,
            });
        };

        var json = await this.tool.ActSequenceAsync(steps: new List<ActionStepDto>());

        var doc = JsonNode.Parse(json)!.AsObject();
        Assert.That(doc["allSucceeded"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stoppedAtIndex"]!.GetValue<int>(), Is.EqualTo(-1));
    }

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnExecuteActionSequence = (_, _, _) =>
            Task.FromResult(new ActionSequenceResultDto
            {
                AllSucceeded = true,
                StoppedAtIndex = -1,
                Steps = new List<ActionStepResultDto>
                {
                    new() { Index = 0, Type = "click", NodeId = "0:1", Success = true },
                },
            });

        var json = await this.tool.ActSequenceAsync(steps: new List<ActionStepDto>());

        Assert.That(json, Does.Contain("\"allSucceeded\""));
        Assert.That(json, Does.Contain("\"stoppedAtIndex\""));
        Assert.That(json, Does.Contain("\"steps\""));
        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Not.Contain("\"AllSucceeded\""));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnExecuteActionSequence = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ActSequenceAsync(new List<ActionStepDto>
            {
                new() { Type = "click", NodeId = "0:1" },
            }));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }
}
