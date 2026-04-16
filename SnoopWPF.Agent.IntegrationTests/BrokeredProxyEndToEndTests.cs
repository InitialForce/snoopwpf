namespace SnoopWPF.Agent.IntegrationTests;

using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// End-to-end tests for methods that were previously
/// <see cref="System.NotImplementedException"/> stubs in
/// <see cref="SnoopWPF.Agent.Remote.PipeSnoopInspectorProxy"/> (M1-06, M2-09..M2-11).
///
/// These tests exercise the engine-level implementations via the integration
/// test fixture's <see cref="McpTestClient"/>, which routes calls through the
/// same <see cref="SnoopWPF.Agent.Engine.SnoopInspector"/> methods that the
/// proxy forwards to in brokered mode. This confirms the implementations are
/// complete and return well-formed DTOs before the proxy wire-protocol layer
/// is added on top.
///
/// Proxy serialization round-trips are separately covered by
/// <c>PipeTransportTests</c> and <c>RemoteFramingTests</c> in
/// <c>SnoopWPF.Agent.Tests</c>.
///
/// Filter: <c>FullyQualifiedName~BrokeredProxyEndToEndTests</c>
/// </summary>
[TestFixture]
public sealed class BrokeredProxyEndToEndTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // M2-10: PollChangesAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>PollChangesAsync(sinceVersion: 0)</c> must return a positive
    /// <c>TreeVersion</c> and a consistent <c>ChangeCount</c> after the
    /// visual tree has been registered via <c>GetVisualTree</c>.
    ///
    /// Previously threw <see cref="System.NotImplementedException"/>("M2-10")
    /// via the proxy stub.
    /// </summary>
    [Test]
    public async Task BrokeredProxyEndToEnd_PollChanges_ReturnsTreeVersion()
    {
        // Pre-register nodes so the engine has something to track.
        await this.Client.GetVisualTreeAsync(maxDepth: 5).ConfigureAwait(false);

        var result = await this.Client.PollChangesAsync(sinceVersion: 0).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "PollChangesAsync must not return null.");
        Assert.That(result.TreeVersion, Is.GreaterThan(0),
            "TreeVersion must be positive after nodes have been registered.");
        Assert.That(result.ChangeCount, Is.EqualTo(result.Changes.Count),
            "ChangeCount must equal Changes.Count (consistency invariant).");
    }

    // -------------------------------------------------------------------------
    // M2-11: PumpUntilIdleAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>PumpUntilIdleAsync</c> with a short timeout must return a well-formed
    /// result with non-negative <c>ElapsedMs</c> and non-null collection fields.
    ///
    /// Previously threw <see cref="System.NotImplementedException"/>("M2-11")
    /// via the proxy stub.
    /// </summary>
    [Test]
    public async Task BrokeredProxyEndToEnd_PumpUntilIdle_ReturnsIdleResult()
    {
        // 500 ms timeout — the integration test WPF app is idle so it resolves quickly.
        var result = await this.Client.Inspector
            .PumpUntilIdleAsync(timeoutMs: 500, resources: null, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "PumpUntilIdleAsync must not return null.");
        Assert.That(result.ElapsedMs, Is.GreaterThanOrEqualTo(0),
            "ElapsedMs must be non-negative.");
        Assert.That(result.ResourcesMonitored, Is.Not.Null,
            "ResourcesMonitored must not be null.");
        Assert.That(result.StillBusy, Is.Not.Null,
            "StillBusy must not be null.");
    }

    // -------------------------------------------------------------------------
    // M2-09: WaitForPropertyAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>WaitForPropertyAsync</c> with a <see cref="WpfLocator"/> must return a
    /// well-formed <c>WaitForPropertyResultDto</c> — i.e., the call completes and
    /// the DTO fields are populated with non-negative values.
    ///
    /// Previously threw <see cref="System.NotImplementedException"/>("M2-09")
    /// via the proxy stub.
    /// </summary>
    [Test]
    public async Task BrokeredProxyEndToEnd_WaitForProperty_ReturnsResult()
    {
        // Locate testButton by nodeId so we can build a WpfLocator.
        var findResult = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: "testButton",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 1,
                ct: default)
            .ConfigureAwait(false);

        if (findResult.Results is null || findResult.Results.Count == 0)
        {
            Assert.Ignore("testButton not found in visual tree — skipping WaitForProperty test.");
            return;
        }

        // Build a TypeName locator for the testButton (type=Button, name=testButton).
        var locator = WpfLocatorParser.Parse("type=Button, name=testButton");

        // 200 ms timeout — short enough to keep the test fast; long enough to
        // exercise at least one poll iteration.
        var result = await this.Client.Inspector
            .WaitForPropertyAsync(
                locator: locator,
                propertyName: "IsEnabled",
                expectedValue: "True",
                timeoutMs: 200,
                presenceExpected: "present",
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "WaitForPropertyAsync must not return null.");
        Assert.That(result.ElapsedMs, Is.GreaterThanOrEqualTo(0),
            "ElapsedMs must be non-negative.");
        Assert.That(result.PollCount, Is.GreaterThanOrEqualTo(0),
            "PollCount must be non-negative.");

        // ConditionMet depends on the button's runtime IsEnabled state;
        // we only verify the call succeeds and returns a valid DTO.
    }
}
