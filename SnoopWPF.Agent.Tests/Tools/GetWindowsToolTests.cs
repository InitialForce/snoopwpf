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
/// Unit tests for <see cref="GetWindowsTool"/>.
/// </summary>
[TestFixture]
public class GetWindowsToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetWindowsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetWindowsTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsWindowList()
    {
        this.fake.OnGetWindows = (includeHidden, ct) =>
            System.Threading.Tasks.Task.FromResult(new List<WindowDto>
            {
                new WindowDto
                {
                    NodeId = "0:1",
                    Title = "Main Window",
                    TypeName = "MainWindow",
                    Width = 800,
                    Height = 600,
                    DispatcherId = 1,
                },
                new WindowDto
                {
                    NodeId = "0:2",
                    Title = "Settings",
                    TypeName = "SettingsWindow",
                    Width = 400,
                    Height = 300,
                    DispatcherId = 1,
                },
            });

        var json = await this.tool.GetWindowsAsync();

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(2));

        var first = doc[0]!;
        Assert.That(first["nodeId"]!.GetValue<string>(), Is.EqualTo("0:1"));
        Assert.That(first["title"]!.GetValue<string>(), Is.EqualTo("Main Window"));
        Assert.That(first["typeName"]!.GetValue<string>(), Is.EqualTo("MainWindow"));
        Assert.That(first["width"]!.GetValue<double>(), Is.EqualTo(800));
        Assert.That(first["height"]!.GetValue<double>(), Is.EqualTo(600));
        Assert.That(first["dispatcherId"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public async Task HappyPath_EmptyWindowList_ReturnsEmptyArray()
    {
        this.fake.OnGetWindows = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<WindowDto>());

        var json = await this.tool.GetWindowsAsync();

        var doc = JsonNode.Parse(json)!.AsArray();
        Assert.That(doc.Count, Is.EqualTo(0));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsIncludeHidden_True()
    {
        bool capturedIncludeHidden = false;

        this.fake.OnGetWindows = (includeHidden, ct) =>
        {
            capturedIncludeHidden = includeHidden;
            return System.Threading.Tasks.Task.FromResult(new List<WindowDto>());
        };

        await this.tool.GetWindowsAsync(includeHidden: true);

        Assert.That(capturedIncludeHidden, Is.True);
    }

    [Test]
    public async Task DefaultParameter_IncludeHiddenIsFalse()
    {
        bool capturedIncludeHidden = true;

        this.fake.OnGetWindows = (includeHidden, ct) =>
        {
            capturedIncludeHidden = includeHidden;
            return System.Threading.Tasks.Task.FromResult(new List<WindowDto>());
        };

        await this.tool.GetWindowsAsync();

        Assert.That(capturedIncludeHidden, Is.False);
    }

    [Test]
    public async Task ForwardsIncludeHidden_False_ExplicitlyPassed()
    {
        bool capturedIncludeHidden = true;

        this.fake.OnGetWindows = (includeHidden, ct) =>
        {
            capturedIncludeHidden = includeHidden;
            return System.Threading.Tasks.Task.FromResult(new List<WindowDto>());
        };

        await this.tool.GetWindowsAsync(includeHidden: false);

        Assert.That(capturedIncludeHidden, Is.False);
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetWindows = (_, _) =>
            System.Threading.Tasks.Task.FromResult(new List<WindowDto>
            {
                new WindowDto { NodeId = "0:1", Title = "T", TypeName = "W", DispatcherId = 1 },
            });

        var json = await this.tool.GetWindowsAsync();

        Assert.That(json, Does.Contain("\"nodeId\""));
        Assert.That(json, Does.Contain("\"typeName\""));
        Assert.That(json, Does.Contain("\"dispatcherId\""));
        Assert.That(json, Does.Not.Contain("\"NodeId\""));
        Assert.That(json, Does.Not.Contain("\"TypeName\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetWindows = (_, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetWindowsAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnGetWindows = (_, _) =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetWindowsAsync());

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void DispatcherBusy_ThrowsMcpException()
    {
        this.fake.OnGetWindows = (_, _) =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher is busy");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetWindowsAsync());

        Assert.That(ex!.Message, Does.Contain("DISPATCHER_BUSY"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
