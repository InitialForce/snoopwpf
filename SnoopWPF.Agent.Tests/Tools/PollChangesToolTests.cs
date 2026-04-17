// SnoopWPF.Agent.Tests/Tools/PollChangesToolTests.cs
// FX6-C2: acceptance tests — LocatorParseException mapped to McpException(INVALID_ARGUMENT).

namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Collections.Generic;
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
/// Tests for <see cref="PollChangesTool"/> — happy path, parameter forwarding, error paths,
/// and FX6-C2 locator parse error mapping.
/// </summary>
[TestFixture]
public class PollChangesToolTests
{
    private FakeSnoopInspector fake = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
    }

    // ── Happy path: no changes ──────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_NoChanges_ReturnsEmptyChangeset()
    {
        this.fake.OnPollChanges = (sinceVersion, locator, ct) =>
            Task.FromResult(new PollChangesResultDto
            {
                TreeVersion = 10,
                SinceVersion = 10,
                Changes = new List<NodeChangeEntryDto>(),
                ChangeCount = 0,
            });

        var tool = new PollChangesTool(this.fake);
        var json = await tool.PollChangesAsync(sinceVersion: 10, ct: CancellationToken.None);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["treeVersion"]!.GetValue<long>(), Is.EqualTo(10));
        Assert.That(doc["changeCount"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(doc["changes"]!.AsArray().Count, Is.EqualTo(0));
    }

    // ── Happy path: with changes ────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_WithChanges_ReturnsChangeset()
    {
        this.fake.OnPollChanges = (sinceVersion, locator, ct) =>
            Task.FromResult(new PollChangesResultDto
            {
                TreeVersion = 15,
                SinceVersion = 10,
                Changes = new List<NodeChangeEntryDto>
                {
                    new NodeChangeEntryDto { NodeId = "0:20", ChangeKind = "added" },
                    new NodeChangeEntryDto { NodeId = "0:5", ChangeKind = "removed" },
                },
                ChangeCount = 2,
            });

        var tool = new PollChangesTool(this.fake);
        var json = await tool.PollChangesAsync(sinceVersion: 10, ct: CancellationToken.None);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["treeVersion"]!.GetValue<long>(), Is.EqualTo(15));
        Assert.That(doc["changeCount"]!.GetValue<int>(), Is.EqualTo(2));

        var changes = doc["changes"]!.AsArray();
        Assert.That(changes.Count, Is.EqualTo(2));
        Assert.That(changes[0]!["nodeId"]!.GetValue<string>(), Is.EqualTo("0:20"));
        Assert.That(changes[0]!["changeKind"]!.GetValue<string>(), Is.EqualTo("added"));
    }

    // ── Parameter forwarding: sinceVersion ─────────────────────────────────────

    [Test]
    public async Task ForwardsSinceVersion_ToInspector()
    {
        long capturedVersion = -1;

        this.fake.OnPollChanges = (sinceVersion, locator, ct) =>
        {
            capturedVersion = sinceVersion;
            return Task.FromResult(new PollChangesResultDto { TreeVersion = sinceVersion, SinceVersion = sinceVersion });
        };

        var tool = new PollChangesTool(this.fake);
        await tool.PollChangesAsync(sinceVersion: 42, ct: CancellationToken.None);

        Assert.That(capturedVersion, Is.EqualTo(42));
    }

    // ── Parameter forwarding: rootLocator parsed and passed ────────────────────

    [Test]
    public async Task ForwardsLocator_WhenRootLocatorProvided()
    {
        WpfLocator? capturedLocator = null;

        this.fake.OnPollChanges = (sinceVersion, locator, ct) =>
        {
            capturedLocator = locator;
            return Task.FromResult(new PollChangesResultDto { TreeVersion = 1, SinceVersion = 0 });
        };

        var tool = new PollChangesTool(this.fake);
        await tool.PollChangesAsync(sinceVersion: 0, rootLocator: "$type:MainWindow", ct: CancellationToken.None);

        Assert.That(capturedLocator, Is.Not.Null);
    }

    // ── JSON camelCase ─────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnPollChanges = (_, _, _) =>
            Task.FromResult(new PollChangesResultDto
            {
                TreeVersion = 5,
                SinceVersion = 3,
                Changes = new List<NodeChangeEntryDto>(),
                ChangeCount = 0,
            });

        var tool = new PollChangesTool(this.fake);
        var json = await tool.PollChangesAsync(sinceVersion: 3, ct: CancellationToken.None);

        Assert.That(json, Does.Contain("\"treeVersion\""));
        Assert.That(json, Does.Contain("\"sinceVersion\""));
        Assert.That(json, Does.Contain("\"changeCount\""));
        Assert.That(json, Does.Not.Contain("\"TreeVersion\""));
        Assert.That(json, Does.Not.Contain("\"ChangeCount\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnPollChanges = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var tool = new PollChangesTool(this.fake);
        var ex = Assert.ThrowsAsync<McpException>(
            () => tool.PollChangesAsync(sinceVersion: 0, ct: CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }

    // ── FX6-C2: LocatorParseException mapped to McpException(INVALID_ARGUMENT) ─

    [Test]
    public void MalformedLocator_ThrowsMcpException()
    {
        // Arrange: rootLocator with invalid syntax that WpfLocatorParser will reject.
        var tool = new PollChangesTool(this.fake);

        // Act / Assert: raw LocatorParseException must NOT escape — it must be
        // mapped to McpException(INVALID_ARGUMENT) by ToolExceptionMapper (FX6-C2).
        var ex = Assert.ThrowsAsync<McpException>(
            () => tool.PollChangesAsync(
                sinceVersion: 0,
                rootLocator: "!!!invalid!!!",
                ct: CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"),
            "A malformed rootLocator must surface as McpException with INVALID_ARGUMENT, not as a raw LocatorParseException.");
    }

    [Test]
    public async Task NullRootLocator_UsesFullTree()
    {
        // Arrange: no rootLocator — should call PollChangesAsync with null locator.
        var tool = new PollChangesTool(this.fake);

        // Act: should not throw with a null/omitted rootLocator.
        var json = await tool.PollChangesAsync(
            sinceVersion: 0,
            rootLocator: null,
            ct: CancellationToken.None).ConfigureAwait(false);

        Assert.That(json, Does.Contain("treeVersion"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnPollChanges = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PollChangesResultDto { TreeVersion = 0, SinceVersion = 0 });
        };

        var tool = new PollChangesTool(this.fake);
        Assert.ThrowsAsync<OperationCanceledException>(
            () => tool.PollChangesAsync(sinceVersion: 0, ct: cts.Token));
    }
}
