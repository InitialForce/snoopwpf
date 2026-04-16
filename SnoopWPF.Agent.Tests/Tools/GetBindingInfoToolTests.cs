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
/// BEAD-010: Tests for <see cref="GetBindingInfoTool"/>.
/// </summary>
[TestFixture]
public class GetBindingInfoToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetBindingInfoTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetBindingInfoTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ActiveBinding_AllFieldsReturned()
    {
        this.fake.OnGetBindingInfo = (nodeId, propertyName, ct) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = true,
                BindingType = "Binding",
                Path = "Title",
                Mode = "TwoWay",
                UpdateSourceTrigger = "PropertyChanged",
                Status = "Active",
                Error = null,
                DataContextIsNull = false,
                DataContextType = "MainViewModel",
                ResolvedValue = "My App",
                ChildBindings = null,
            });

        var json = await this.tool.GetBindingInfoAsync("0:1", "Text");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasBinding"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["bindingType"]!.GetValue<string>(), Is.EqualTo("Binding"));
        Assert.That(doc["path"]!.GetValue<string>(), Is.EqualTo("Title"));
        Assert.That(doc["mode"]!.GetValue<string>(), Is.EqualTo("TwoWay"));
        Assert.That(doc["status"]!.GetValue<string>(), Is.EqualTo("Active"));
        Assert.That(doc["dataContextIsNull"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["dataContextType"]!.GetValue<string>(), Is.EqualTo("MainViewModel"));
        Assert.That(doc["resolvedValue"]!.GetValue<string>(), Is.EqualTo("My App"));
    }

    [Test]
    public async Task HappyPath_BindingError_ErrorFieldPopulated()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = true,
                BindingType = "Binding",
                Path = "BadPath",
                Status = "PathError",
                Error = "Cannot resolve property 'BadPath'",
                DataContextIsNull = false,
                DataContextType = "SomeViewModel",
            });

        var json = await this.tool.GetBindingInfoAsync("0:3", "Text");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["status"]!.GetValue<string>(), Is.EqualTo("PathError"));
        Assert.That(doc["error"]!.GetValue<string>(), Does.Contain("Cannot resolve"));
    }

    [Test]
    public async Task HappyPath_DataContextIsNull_Reflected()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = true,
                DataContextIsNull = true,
                DataContextType = string.Empty,
                ResolvedValue = null,
            });

        var json = await this.tool.GetBindingInfoAsync("0:5", "Text");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["dataContextIsNull"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task HappyPath_NoBinding_HasBindingFalse()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = false,
            });

        var json = await this.tool.GetBindingInfoAsync("0:7", "Background");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasBinding"]!.GetValue<bool>(), Is.False);
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsNodeIdAndPropertyName()
    {
        string? capturedNodeId = null;
        string? capturedProp = null;

        this.fake.OnGetBindingInfo = (nodeId, propertyName, ct) =>
        {
            capturedNodeId = nodeId;
            capturedProp = propertyName;
            return Task.FromResult(new BindingInfoDto());
        };

        await this.tool.GetBindingInfoAsync("0:42", "IsEnabled");

        Assert.That(capturedNodeId, Is.EqualTo("0:42"));
        Assert.That(capturedProp, Is.EqualTo("IsEnabled"));
    }

    // ── Redacted property ────────────────────────────────────────────────────────

    [Test]
    public async Task RedactedProperty_PathAndResolvedValueAreRedacted()
    {
        // Per BEAD-003-inspector §11: redacted property binding info returns [REDACTED]
        // for Path and ResolvedValue. The tool handler passes through what the inspector returns.
        this.fake.OnGetBindingInfo = (_, _, _) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = true,
                Path = "[REDACTED]",
                Mode = "TwoWay",
                ResolvedValue = "[REDACTED]",
                Status = "Active",
            });

        var json = await this.tool.GetBindingInfoAsync("0:1", "Password");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["path"]!.GetValue<string>(), Is.EqualTo("[REDACTED]"));
        Assert.That(doc["resolvedValue"]!.GetValue<string>(), Is.EqualTo("[REDACTED]"));
        Assert.That(doc["mode"]!.GetValue<string>(), Is.EqualTo("TwoWay"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            Task.FromResult(new BindingInfoDto
            {
                HasBinding = true,
                DataContextIsNull = false,
                UpdateSourceTrigger = "PropertyChanged",
            });

        var json = await this.tool.GetBindingInfoAsync("0:1", "Text");

        Assert.That(json, Does.Contain("\"hasBinding\""));
        Assert.That(json, Does.Contain("\"dataContextIsNull\""));
        Assert.That(json, Does.Contain("\"updateSourceTrigger\""));
        Assert.That(json, Does.Not.Contain("\"HasBinding\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetBindingInfoAsync("0:99", "Text"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void DispatcherBusy_ThrowsMcpException()
    {
        this.fake.OnGetBindingInfo = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher busy");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetBindingInfoAsync("0:1", "Text"));

        Assert.That(ex!.Message, Does.Contain("DISPATCHER_BUSY"));
    }
}
