namespace SnoopWPF.Agent.Tests.Tools;

using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Tests for <see cref="WaitForPropertyTool"/> — including FX6-A1 timeout ceiling enforcement.
/// </summary>
[TestFixture]
public class WaitForPropertyToolTests
{
    private FakeSnoopInspector fake = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
    }

    // ── FX6-A1: Timeout clamping ──────────────────────────────────────────────

    [Test]
    public void TimeoutClampedToCeiling_RejectsTooLargeTimeout()
    {
        // Arrange: default ceiling is 30000 ms; request 86400000 (24 h).
        var options = new SnoopAgentOptions(); // MaxWaitForPropertyMs = 30_000 by default
        var tool = new WaitForPropertyTool(this.fake, options);

        // Act / Assert: should throw McpException before ever calling the inspector.
        var ex = Assert.ThrowsAsync<McpException>(
            () => tool.WaitForPropertyAsync(
                locator: "type=Button",
                propertyName: "IsEnabled",
                expectedValue: "True",
                timeoutMs: 86_400_000,
                presenceExpected: "present",
                ct: CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"));
        Assert.That(ex.Message, Does.Contain("30000"));
    }

    [Test]
    public async Task TimeoutAtCeiling_IsAllowed()
    {
        // Arrange: timeoutMs exactly at ceiling should pass through to the inspector.
        var options = new SnoopAgentOptions(); // MaxWaitForPropertyMs = 30_000
        var tool = new WaitForPropertyTool(this.fake, options);

        this.fake.ConfigureLocatorDelegate<WaitForPropertyResultDto>(
            (locator, ct) => Task.FromResult(new WaitForPropertyResultDto
            {
                ConditionMet = true,
                ActualValue = "True",
                ElapsedMs = 10,
                PollCount = 1,
            }));

        // Should not throw for exactly the ceiling value.
        var json = await tool.WaitForPropertyAsync(
            locator: "type=Button",
            propertyName: "IsEnabled",
            expectedValue: "True",
            timeoutMs: 30_000,
            presenceExpected: "present",
            ct: CancellationToken.None);

        Assert.That(json, Does.Contain("conditionMet"));
    }

    [Test]
    public async Task TimeoutBelowCeiling_IsAllowed()
    {
        // Arrange: timeoutMs below ceiling should pass through to the inspector.
        var options = new SnoopAgentOptions();
        var tool = new WaitForPropertyTool(this.fake, options);

        this.fake.ConfigureLocatorDelegate<WaitForPropertyResultDto>(
            (locator, ct) => Task.FromResult(new WaitForPropertyResultDto
            {
                ConditionMet = false,
                ActualValue = null,
                ElapsedMs = 100,
                PollCount = 1,
            }));

        var json = await tool.WaitForPropertyAsync(
            locator: "type=Button",
            propertyName: "IsEnabled",
            expectedValue: "True",
            timeoutMs: 5_000,
            presenceExpected: "present",
            ct: CancellationToken.None);

        Assert.That(json, Does.Contain("conditionMet"));
    }

    [Test]
    public void TimeoutClampedToCeiling_CustomCeiling_RejectsAboveCeiling()
    {
        // Arrange: custom ceiling of 5000 ms.
        var options = new SnoopAgentOptions { MaxWaitForPropertyMs = 5_000 };
        var tool = new WaitForPropertyTool(this.fake, options);

        var ex = Assert.ThrowsAsync<McpException>(
            () => tool.WaitForPropertyAsync(
                locator: "type=Button",
                propertyName: "IsEnabled",
                expectedValue: "True",
                timeoutMs: 10_000,
                presenceExpected: "present",
                ct: CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("5000"));
    }

    // ── bd-1a9.20: CONDITION_NOT_MET warning surface ──────────────────────────────

    [Test]
    public async Task ConditionNotMet_EmitsWarningInJson()
    {
        // Arrange: inspector returns conditionMet=false (simulated timeout).
        var options = new SnoopAgentOptions();
        var tool = new WaitForPropertyTool(this.fake, options);

        this.fake.ConfigureLocatorDelegate<WaitForPropertyResultDto>(
            (locator, ct) => Task.FromResult(new WaitForPropertyResultDto
            {
                ConditionMet = false,
                ActualValue = "False",
                ElapsedMs = 5000,
                PollCount = 50,
            }));

        // Act
        var json = await tool.WaitForPropertyAsync(
            locator: "type=Button",
            propertyName: "IsEnabled",
            expectedValue: "True",
            timeoutMs: 5_000,
            presenceExpected: "present",
            ct: System.Threading.CancellationToken.None);

        // Assert: warnings[] is present and contains CONDITION_NOT_MET (bd-1a9.20 AC).
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var warnings = node["warnings"]?.AsArray();
        Assert.That(warnings, Is.Not.Null, "warnings[] must be present when conditionMet=false");
        Assert.That(warnings!.Count, Is.EqualTo(1));
        Assert.That(warnings[0]!.GetValue<string>(), Does.StartWith("[CONDITION_NOT_MET]"));
        Assert.That(warnings[0]!.GetValue<string>(), Does.Contain("IsEnabled"));
    }

    [Test]
    public async Task ConditionMet_NoWarningInJson()
    {
        // Arrange: inspector returns conditionMet=true (no warning expected).
        var options = new SnoopAgentOptions();
        var tool = new WaitForPropertyTool(this.fake, options);

        this.fake.ConfigureLocatorDelegate<WaitForPropertyResultDto>(
            (locator, ct) => Task.FromResult(new WaitForPropertyResultDto
            {
                ConditionMet = true,
                ActualValue = "True",
                ElapsedMs = 100,
                PollCount = 2,
            }));

        var json = await tool.WaitForPropertyAsync(
            locator: "type=Button",
            propertyName: "IsEnabled",
            expectedValue: "True",
            timeoutMs: 5_000,
            presenceExpected: "present",
            ct: System.Threading.CancellationToken.None);

        // Assert: no warnings when condition was satisfied (bd-1a9.20 AC — omit when empty).
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        Assert.That(node["warnings"], Is.Null, "warnings[] must be absent when conditionMet=true");
    }

    // ── FX6-C2: LocatorParseException mapped to McpException(INVALID_ARGUMENT) ─

    [Test]
    public void MalformedLocator_ThrowsMcpException()
    {
        // Arrange: locator with invalid syntax that WpfLocatorParser will reject.
        var options = new SnoopAgentOptions();
        var tool = new WaitForPropertyTool(this.fake, options);

        // Act / Assert: raw LocatorParseException must NOT escape — it must be
        // mapped to McpException(INVALID_ARGUMENT) by ToolExceptionMapper (FX6-C2).
        var ex = Assert.ThrowsAsync<McpException>(
            () => tool.WaitForPropertyAsync(
                locator: "!!!invalid!!!",
                propertyName: "IsEnabled",
                expectedValue: "True",
                timeoutMs: 5_000,
                presenceExpected: "present",
                ct: CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("INVALID_ARGUMENT"),
            "A malformed locator must surface as McpException with INVALID_ARGUMENT, not as a raw LocatorParseException.");
    }
}

/// <summary>
/// Tests for <see cref="SnoopAgentOptions"/> — FX6-A1 default value validation.
/// </summary>
[TestFixture]
public class SnoopAgentOptionsTests
{
    [Test]
    public void MaxWaitForPropertyMs_DefaultMatchesPrd()
    {
        // PRD FX6-A1: default must be 30 000 ms.
        var options = new SnoopAgentOptions();
        Assert.That(options.MaxWaitForPropertyMs, Is.EqualTo(30_000));
    }
}
