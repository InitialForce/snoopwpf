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
/// Unit tests for <see cref="SetPropertyTool"/>.
/// </summary>
[TestFixture]
public class SetPropertyToolTests
{
    private FakeSnoopInspector fake = null!;

    private SetPropertyTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SetPropertyTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSetProperty = (nodeId, propertyName, value, ct) =>
            System.Threading.Tasks.Task.FromResult(new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = "White",
                NewValue = "Red",
            });

        var json = await this.tool.SetPropertyAsync("0:1", "Background", "Red");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["previousValue"]!.GetValue<string>(), Is.EqualTo("White"));
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("Red"));
    }

    [Test]
    public async Task StateUnchanged_ReturnsSuccessFalse_WithFailureReason()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new StateDeltaDto
            {
                Success = false,
                ElementVisible = true,
                StateChanged = false,
                FailureReason = FailureReason.StateUnchanged,
                Suggestion = new SuggestionDto
                {
                    Tool = "wpf_inspect_element",
                    Args = new System.Collections.Generic.List<NameValuePairDto>
                    {
                        new() { Name = "nodeId", Value = "0:2" },
                    },
                },
                PreviousValue = "100",
                NewValue = "100",
            });

        var json = await this.tool.SetPropertyAsync("0:2", "Width", "100");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["failureReason"]!.GetValue<int>(), Is.EqualTo((int)FailureReason.StateUnchanged));
        Assert.That(doc["suggestion"]!["tool"]!.GetValue<string>(), Is.EqualTo("wpf_inspect_element"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedPropertyName = null;
        string? capturedValue = null;

        this.fake.OnSetProperty = (nodeId, propertyName, value, ct) =>
        {
            capturedNodeId = nodeId;
            capturedPropertyName = propertyName;
            capturedValue = value;
            return System.Threading.Tasks.Task.FromResult(new StateDeltaDto { Success = true, StateChanged = true });
        };

        await this.tool.SetPropertyAsync("0:5", "Visibility", "Collapsed");

        Assert.That(capturedNodeId, Is.EqualTo("0:5"));
        Assert.That(capturedPropertyName, Is.EqualTo("Visibility"));
        Assert.That(capturedValue, Is.EqualTo("Collapsed"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = "old",
                NewValue = "new",
            });

        var json = await this.tool.SetPropertyAsync("0:1", "Tag", "new");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Contain("\"elementVisible\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetPropertyAsync("0:99", "Width", "200"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation is not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetPropertyAsync("0:1", "Width", "200"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void PropertyReadOnly_ThrowsMcpException()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.PropertyReadOnly, "Property is read-only");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetPropertyAsync("0:1", "ActualWidth", "200"));

        Assert.That(ex!.Message, Does.Contain("PROPERTY_READ_ONLY"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void PropertyRedacted_ThrowsMcpException()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.PropertyRedacted, "Property is redacted");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetPropertyAsync("0:1", "Password", "secret"));

        Assert.That(ex!.Message, Does.Contain("PROPERTY_REDACTED"));
    }

    [Test]
    public void TypeConversionFailed_ThrowsMcpException()
    {
        this.fake.OnSetProperty = (_, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.TypeConversionFailed, "Cannot convert value");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SetPropertyAsync("0:1", "Width", "notanumber"));

        Assert.That(ex!.Message, Does.Contain("TYPE_CONVERSION_FAILED"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
