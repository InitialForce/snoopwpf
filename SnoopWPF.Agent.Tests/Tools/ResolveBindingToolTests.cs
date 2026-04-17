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
/// Unit tests for <see cref="ResolveBindingTool"/>.
/// </summary>
[TestFixture]
public class ResolveBindingToolTests
{
    private FakeSnoopInspector fake = null!;

    private ResolveBindingTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new ResolveBindingTool(this.fake);
    }

    // ── Happy path: binding found ───────────────────────────────────────────────

    [Test]
    public async Task HappyPath_WithBinding_ReturnsResolutionDto()
    {
        this.fake.OnResolveBinding = (nodeId, propertyName, ct) =>
            Task.FromResult(new BindingResolutionDto
            {
                HasBinding = true,
                Path = "ViewModel.UserName",
                SourceTypeName = "MainViewModel",
                Status = "OK",
            });

        var json = await this.tool.ResolveBindingAsync("0:5", "Text");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasBinding"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["path"]!.GetValue<string>(), Is.EqualTo("ViewModel.UserName"));
        Assert.That(doc["status"]!.GetValue<string>(), Is.EqualTo("OK"));
    }

    // ── Happy path: no binding ──────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_NoBinding_ReturnsNoBindingStatus()
    {
        this.fake.OnResolveBinding = (nodeId, propertyName, ct) =>
            Task.FromResult(new BindingResolutionDto
            {
                HasBinding = false,
                Status = "NoBinding",
            });

        var json = await this.tool.ResolveBindingAsync("0:3", "IsEnabled");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["hasBinding"]!.GetValue<bool>(), Is.False);
        Assert.That(doc["status"]!.GetValue<string>(), Is.EqualTo("NoBinding"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedPropertyName = null;

        this.fake.OnResolveBinding = (nodeId, propertyName, ct) =>
        {
            capturedNodeId = nodeId;
            capturedPropertyName = propertyName;
            return Task.FromResult(new BindingResolutionDto { Status = "OK" });
        };

        await this.tool.ResolveBindingAsync("0:7", "Text");

        Assert.That(capturedNodeId, Is.EqualTo("0:7"));
        Assert.That(capturedPropertyName, Is.EqualTo("Text"));
    }

    // ── Error path: NodeNotFound ────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnResolveBinding = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ResolveBindingAsync("0:99", "Text"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnResolveBinding = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BindingResolutionDto { Status = "OK" });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.ResolveBindingAsync("0:1", "Text", ct: cts.Token));
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnResolveBinding = (_, _, _) =>
            Task.FromResult(new BindingResolutionDto
            {
                HasBinding = true,
                Path = "Name",
                SourceTypeName = "ViewModel",
                Status = "OK",
            });

        var json = await this.tool.ResolveBindingAsync("0:1", "Text");

        Assert.That(json, Does.Contain("\"hasBinding\""));
        Assert.That(json, Does.Contain("\"path\""));
        Assert.That(json, Does.Contain("\"sourceTypeName\""));
        Assert.That(json, Does.Contain("\"status\""));
        Assert.That(json, Does.Not.Contain("\"HasBinding\""));
        Assert.That(json, Does.Not.Contain("\"Status\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnResolveBinding = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.ResolveBindingAsync("0:1", "Text"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
