namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="SetSliderValueTool"/>.
/// </summary>
[TestFixture]
public class SetSliderValueToolTests
{
    private FakeSnoopInspector fake = null!;

    private SetSliderValueTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SetSliderValueTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSetSliderValue = (nodeId, value, normalized, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = "0",
                NewValue = "75",
            });

        var json = await this.tool.SetSliderValueAsync("0:1", 75.0);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["previousValue"]!.GetValue<string>(), Is.EqualTo("0"));
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("75"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSetSliderValue = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetSliderValueAsync("0:99", 50.0));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: Success=false with failure reason ───────────────────────────

    [Test]
    public async Task FailurePath_NodeNotFoundInDto_SurfacesFailureReason()
    {
        this.fake.OnSetSliderValue = (_, _, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = false,
                ElementVisible = false,
                StateChanged = false,
                FailureReason = FailureReason.ElementNotFound,
                Suggestion = new SuggestionDto
                {
                    Tool = "wpf_find_elements",
                    Args = new System.Collections.Generic.List<NameValuePairDto>
                    {
                        new() { Name = "query", Value = "Slider" },
                    },
                },
            });

        var json = await this.tool.SetSliderValueAsync("0:99", 50.0);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["failureReason"]!.GetValue<string>(), Is.EqualTo("ELEMENT_NOT_FOUND"));
        Assert.That(doc["suggestion"]!["tool"]!.GetValue<string>(), Is.EqualTo("wpf_find_elements"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSetSliderValue = (_, _, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SetSliderValueAsync("0:1", 50.0, ct: cts.Token));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        double capturedValue = 0;
        bool capturedNormalized = false;

        this.fake.OnSetSliderValue = (nodeId, value, normalized, ct) =>
        {
            capturedNodeId = nodeId;
            capturedValue = value;
            capturedNormalized = normalized;
            return Task.FromResult(new StateDeltaDto { Success = true, StateChanged = true });
        };

        await this.tool.SetSliderValueAsync("0:7", 0.5, normalized: true);

        Assert.That(capturedNodeId, Is.EqualTo("0:7"));
        Assert.That(capturedValue, Is.EqualTo(0.5));
        Assert.That(capturedNormalized, Is.True);
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSetSliderValue = (_, _, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = "0",
                NewValue = "50",
            });

        var json = await this.tool.SetSliderValueAsync("0:1", 50.0);

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Contain("\"elementVisible\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── Error mapping: MutationDisabled ────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSetSliderValue = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation is not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetSliderValueAsync("0:1", 50.0));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
