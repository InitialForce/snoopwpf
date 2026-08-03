// SnoopWPF.Agent.Tests/Tools/WpfDiagnosticsToolTests.cs
// FX6-D3: acceptance tests for WpfDiagnosticsTool.

namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Blob;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="WpfDiagnosticsTool"/> (FX6-D3).
/// </summary>
[TestFixture]
public sealed class WpfDiagnosticsToolTests : IDisposable
{
    private FakeSnoopInspector fake = null!;

    private BlobStore blobStore = null!;

    private SessionPolicy policy = null!;

    private AgentStartInfo startInfo = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.blobStore = new BlobStore(TimeSpan.FromSeconds(60));
        this.policy = SessionPolicy.Create(
            SessionMode.CoLocated,
            new SnoopAgentOptions
            {
                EnableMutation = true,
                EnableAutomation = false,
                AllowSensitiveRetention = false,
            });
        this.startInfo = new AgentStartInfo(DateTimeOffset.UtcNow.AddSeconds(-30));
    }

    [TearDown]
    public void TearDown()
    {
        this.blobStore.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.blobStore?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private WpfDiagnosticsTool MakeTool()
        => new WpfDiagnosticsTool(this.fake, this.blobStore, this.policy, this.startInfo);

    private static AgentDiagnosticsDto Deserialize(string json)
    {
        var doc = JsonDocument.Parse(json);
        return new AgentDiagnosticsDto
        {
            AgentVersion = doc.RootElement.GetProperty("agentVersion").GetString() ?? string.Empty,
            Mode = doc.RootElement.GetProperty("mode").GetString() ?? string.Empty,
            DispatcherHealthy = doc.RootElement.GetProperty("dispatcherHealthy").GetBoolean(),
            DispatcherQueueLength = doc.RootElement.GetProperty("dispatcherQueueLength").GetInt32(),
            BlobStoreCount = doc.RootElement.GetProperty("blobStoreCount").GetInt32(),
            BlobStoreBytes = doc.RootElement.GetProperty("blobStoreBytes").GetInt64(),
            AuditLogDepth = doc.RootElement.GetProperty("auditLogDepth").GetInt32(),
            SessionPolicy = new SessionPolicySnapshotDto
            {
                EnableMutation = doc.RootElement.GetProperty("sessionPolicy").GetProperty("enableMutation").GetBoolean(),
                EnableAutomation = doc.RootElement.GetProperty("sessionPolicy").GetProperty("enableAutomation").GetBoolean(),
                AllowSensitiveRetention = doc.RootElement.GetProperty("sessionPolicy").GetProperty("allowSensitiveRetention").GetBoolean(),
            },
            UptimeSeconds = doc.RootElement.GetProperty("uptimeSeconds").GetDouble(),
        };
    }

    // -------------------------------------------------------------------------
    // agentVersion field is present and non-empty
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_AgentVersion_NonEmpty()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto());

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.AgentVersion, Is.Not.Null.And.Not.Empty,
            "agentVersion must be a non-empty string.");
    }

    // -------------------------------------------------------------------------
    // mode field reflects session policy mode
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_Mode_CoLocated()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto());

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.Mode, Is.EqualTo("CoLocated"),
            "mode must match the SessionPolicy.Mode.");
    }

    // -------------------------------------------------------------------------
    // dispatcherHealthy: true when inspector responds
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_DispatcherHealthy_True_WhenInspectorResponds()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto { ProcessName = "test" });

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.DispatcherHealthy, Is.True,
            "dispatcherHealthy must be true when GetSessionInfoAsync succeeds.");
    }

    // -------------------------------------------------------------------------
    // dispatcherHealthy: false when inspector throws DispatcherBusy
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_DispatcherHealthy_False_WhenDispatcherBusy()
    {
        this.fake.OnGetSessionInfo = ct =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher is busy.");

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.DispatcherHealthy, Is.False,
            "dispatcherHealthy must be false when GetSessionInfoAsync throws DispatcherBusy.");
    }

    // -------------------------------------------------------------------------
    // blobStoreCount and blobStoreBytes
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_BlobStoreFields_ReflectCurrentState()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto());

        // Seed the BlobStore with one entry.
        this.blobStore.Store("test-key", new byte[] { 1, 2, 3, 4 }, "application/octet-stream");

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.BlobStoreCount, Is.EqualTo(1),
            "blobStoreCount must reflect the number of blobs currently stored.");
        Assert.That(dto.BlobStoreBytes, Is.EqualTo(4L),
            "blobStoreBytes must reflect the total byte size of all stored blobs.");
    }

    // -------------------------------------------------------------------------
    // uptimeSeconds > 0
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_UptimeSeconds_GreaterThanZero()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto());

        // startInfo was created with StartedAt = UtcNow - 30s in SetUp.
        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.UptimeSeconds, Is.GreaterThan(0),
            "uptimeSeconds must be positive when agent has been running for 30 seconds.");
    }

    // -------------------------------------------------------------------------
    // sessionPolicy fields
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_SessionPolicy_Fields()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto());

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);
        var dto = Deserialize(json);

        Assert.That(dto.SessionPolicy.EnableMutation, Is.True,
            "sessionPolicy.enableMutation must reflect the session policy.");
        Assert.That(dto.SessionPolicy.EnableAutomation, Is.False,
            "sessionPolicy.enableAutomation must reflect the session policy.");
        Assert.That(dto.SessionPolicy.AllowSensitiveRetention, Is.False,
            "sessionPolicy.allowSensitiveRetention must reflect the session policy.");
    }

    // -------------------------------------------------------------------------
    // sessionInfo is folded into the diagnostics response (session-info tool merge)
    // -------------------------------------------------------------------------

    [Test]
    public async Task Returns_SessionInfo_FromDispatcherProbe()
    {
        this.fake.OnGetSessionInfo = ct =>
            Task.FromResult(new SessionInfoDto
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

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var sessionInfo = doc.RootElement.GetProperty("sessionInfo");
        Assert.That(sessionInfo.ValueKind, Is.EqualTo(JsonValueKind.Object),
            "sessionInfo must be populated when the Dispatcher probe succeeds.");
        Assert.That(sessionInfo.GetProperty("processName").GetString(), Is.EqualTo("MyApp"));
        Assert.That(sessionInfo.GetProperty("pid").GetInt32(), Is.EqualTo(1234));
        Assert.That(sessionInfo.GetProperty("dotnetVersion").GetString(), Is.EqualTo("8.0.0"));
        Assert.That(sessionInfo.GetProperty("mutationEnabled").GetBoolean(), Is.True);

        var dispatchers = sessionInfo.GetProperty("dispatchers");
        Assert.That(dispatchers.GetArrayLength(), Is.EqualTo(1));
        var windowNodeIds = dispatchers[0].GetProperty("windowNodeIds");
        Assert.That(windowNodeIds.GetArrayLength(), Is.EqualTo(2),
            "sessionInfo must carry the bootstrap window node IDs.");
        Assert.That(sessionInfo.GetProperty("capabilities").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task Returns_SessionInfo_Null_WhenDispatcherUnhealthy()
    {
        this.fake.OnGetSessionInfo = ct =>
            throw new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher is busy.");

        var tool = this.MakeTool();
        var json = await tool.GetDiagnosticsAsync(default).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("dispatcherHealthy").GetBoolean(), Is.False);
        Assert.That(doc.RootElement.GetProperty("sessionInfo").ValueKind, Is.EqualTo(JsonValueKind.Null),
            "sessionInfo must be null when the Dispatcher probe fails.");
    }
}
