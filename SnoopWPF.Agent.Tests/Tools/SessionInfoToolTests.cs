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
/// Unit tests for <see cref="SessionInfoTool"/>.
/// </summary>
[TestFixture]
public class SessionInfoToolTests
{
    private FakeSnoopInspector fake = null!;

    private SessionInfoTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SessionInfoTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsSessionInfo()
    {
        this.fake.OnGetSessionInfo = ct =>
            System.Threading.Tasks.Task.FromResult(new SessionInfoDto
            {
                ProcessName = "MyApp",
                Pid = 1234,
                DotnetVersion = "8.0.0",
                MutationEnabled = true,
                Dispatchers = new List<DispatcherInfoDto>
                {
                    new DispatcherInfoDto
                    {
                        Id = 1,
                        ThreadId = 5,
                        WindowNodeIds = new List<string> { "0:1", "0:2" },
                    },
                },
                Capabilities = new List<string> { "screenshot", "behaviors" },
            });

        var json = await this.tool.GetSessionInfoAsync(default);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["processName"]!.GetValue<string>(), Is.EqualTo("MyApp"));
        Assert.That(doc["pid"]!.GetValue<int>(), Is.EqualTo(1234));
        Assert.That(doc["dotnetVersion"]!.GetValue<string>(), Is.EqualTo("8.0.0"));
        Assert.That(doc["mutationEnabled"]!.GetValue<bool>(), Is.True);

        var dispatchers = doc["dispatchers"]!.AsArray();
        Assert.That(dispatchers.Count, Is.EqualTo(1));
        Assert.That(dispatchers[0]!["id"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(dispatchers[0]!["threadId"]!.GetValue<int>(), Is.EqualTo(5));

        var windowNodeIds = dispatchers[0]!["windowNodeIds"]!.AsArray();
        Assert.That(windowNodeIds.Count, Is.EqualTo(2));

        var caps = doc["capabilities"]!.AsArray();
        Assert.That(caps.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task HappyPath_MutationDisabled_ReflectedInOutput()
    {
        this.fake.OnGetSessionInfo = ct =>
            System.Threading.Tasks.Task.FromResult(new SessionInfoDto
            {
                ProcessName = "ReadOnlyApp",
                Pid = 999,
                MutationEnabled = false,
                Capabilities = new List<string>(),
            });

        var json = await this.tool.GetSessionInfoAsync(default);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["mutationEnabled"]!.GetValue<bool>(), Is.False);
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetSessionInfo = ct =>
            System.Threading.Tasks.Task.FromResult(new SessionInfoDto
            {
                ProcessName = "P",
                MutationEnabled = false,
            });

        var json = await this.tool.GetSessionInfoAsync(default);

        Assert.That(json, Does.Contain("\"processName\""));
        Assert.That(json, Does.Contain("\"mutationEnabled\""));
        Assert.That(json, Does.Contain("\"dotnetVersion\""));
        Assert.That(json, Does.Not.Contain("\"ProcessName\""));
        Assert.That(json, Does.Not.Contain("\"MutationEnabled\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnGetSessionInfo = ct =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetSessionInfoAsync(default));

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void OperationTimedOut_ThrowsMcpException()
    {
        this.fake.OnGetSessionInfo = ct =>
            throw new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetSessionInfoAsync(default));

        Assert.That(ex!.Message, Does.Contain("OPERATION_TIMED_OUT"));
    }

    [Test]
    public void ProtocolMismatch_ThrowsMcpException()
    {
        this.fake.OnGetSessionInfo = ct =>
            throw new SnoopException(SnoopErrorCode.ProtocolMismatch, "Protocol version mismatch");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetSessionInfoAsync(default));

        Assert.That(ex!.Message, Does.Contain("PROTOCOL_MISMATCH"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }
}
