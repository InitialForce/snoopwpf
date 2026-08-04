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
/// Unit tests for the consolidated <see cref="SelectItemTool"/>, covering all three
/// selection modes (by identifier, by exact index, by index with scroll-to-realize)
/// and the discriminator validation.
/// </summary>
[TestFixture]
public class SelectItemToolTests
{
    private FakeSnoopInspector fake = null!;

    private SelectItemTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new SelectItemTool(this.fake);
    }

    // ── Identifier mode ──────────────────────────────────────────────────────────

    [Test]
    public async Task Identifier_HappyPath_ReturnsStateDelta()
    {
        this.fake.OnSelectItem = (nodeId, identifier, ct) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
            });

        var json = await this.tool.SelectItemAsync("0:5", identifier: "Item A");

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task Identifier_ForwardsParameters_ToInspector()
    {
        string? capturedNodeId = null;
        string? capturedIdentifier = null;

        this.fake.OnSelectItem = (nodeId, identifier, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIdentifier = identifier;
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        await this.tool.SelectItemAsync("0:8", identifier: "2");

        Assert.That(capturedNodeId, Is.EqualTo("0:8"));
        Assert.That(capturedIdentifier, Is.EqualTo("2"));
    }

    // ── Index mode (no scroll) ─────────────────────────────────────────────────────

    [Test]
    public async Task Index_HappyPath_RoutesToSelectByIndex()
    {
        string? capturedNodeId = null;
        int capturedIndex = -1;

        this.fake.OnSelectItemByIndex = (nodeId, index, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIndex = index;
            return Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "0",
                NewValue = "5",
            });
        };
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new InvalidOperationException("scroll path must not be used when scrollToRealize=false");

        var json = await this.tool.SelectItemAsync("0:5", index: 5);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["stateChanged"]!.GetValue<bool>(), Is.True);
        Assert.That(capturedNodeId, Is.EqualTo("0:5"));
        Assert.That(capturedIndex, Is.EqualTo(5));
    }

    [Test]
    public void Index_InvalidArgument_ThrowsMcpException()
    {
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.InvalidArgument, "index 9999 is out of range");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", index: 9999));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Index mode with scroll-to-realize ──────────────────────────────────────────

    [Test]
    public async Task IndexScroll_RoutesToSelectByScroll()
    {
        string? capturedNodeId = null;
        int capturedIndex = -1;

        this.fake.OnSelectItemByScroll = (nodeId, targetIndex, ct) =>
        {
            capturedNodeId = nodeId;
            capturedIndex = targetIndex;
            return Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                PreviousValue = "Item A",
                NewValue = "Item B",
            });
        };
        this.fake.OnSelectItemByIndex = (_, _, _) =>
            throw new InvalidOperationException("index path must not be used when scrollToRealize=true");

        var json = await this.tool.SelectItemAsync("0:8", index: 500, scrollToRealize: true);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["success"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["newValue"]!.GetValue<string>(), Is.EqualTo("Item B"));
        Assert.That(capturedNodeId, Is.EqualTo("0:8"));
        Assert.That(capturedIndex, Is.EqualTo(500));
    }

    [Test]
    public void IndexScroll_ElementOutsideViewport_ThrowsMcpException()
    {
        this.fake.OnSelectItemByScroll = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.InvalidArgument, "targetIndex 9999 is out of range");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", index: 9999, scrollToRealize: true));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Discriminator validation ────────────────────────────────────────────────────

    [Test]
    public void NeitherIdentifierNorIndex_ThrowsInvalidArgument()
    {
        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    [Test]
    public void IdentifierWithScrollToRealize_ThrowsInvalidArgument_NotSilentlyDropped()
    {
        // scrollToRealize is index-mode only; passing it with identifier used to be silently ignored
        // (a virtualized-list selection would fail as not-found with no explanation). It must be a
        // loud INVALID_ARGUMENT instead.
        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", identifier: "Item 900", scrollToRealize: true));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    [Test]
    public void BothIdentifierAndIndex_ThrowsInvalidArgument()
    {
        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", identifier: "Item A", index: 3));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
    }

    // ── Error path: NodeNotFound (identifier mode) ─────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:99", identifier: "Item"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    // ── Error path: MutationDisabled ───────────────────────────────────────────────

    [Test]
    public void MutationDisabled_ThrowsMcpException()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation not enabled");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", identifier: "0"));

        Assert.That(ex!.Message, Does.Contain("MUTATION_DISABLED"));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────────

    [Test]
    public void Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        this.fake.OnSelectItem = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StateDeltaDto { Success = true });
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            () => this.tool.SelectItemAsync("0:1", identifier: "Item", ct: cts.Token));
    }

    // ── JSON camelCase ───────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            Task.FromResult(new StateDeltaDto
            {
                Success = true,
                StateChanged = true,
                ElementVisible = true,
            });

        var json = await this.tool.SelectItemAsync("0:1", identifier: "0");

        Assert.That(json, Does.Contain("\"success\""));
        Assert.That(json, Does.Contain("\"stateChanged\""));
        Assert.That(json, Does.Not.Contain("\"Success\""));
        Assert.That(json, Does.Not.Contain("\"StateChanged\""));
    }

    // ── ToolExceptionMapper: ODE → AGENT_DISPOSED ──────────────────────────────────

    [Test]
    public void ObjectDisposedException_WrappedToAgentDisposed()
    {
        this.fake.OnSelectItem = (_, _, _) =>
            throw new System.ObjectDisposedException("SnoopInspector");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.SelectItemAsync("0:1", identifier: "Item"));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"));
    }
}
