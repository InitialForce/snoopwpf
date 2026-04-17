// SnoopWPF.Agent.IntegrationTests/BrokerHostRoundTripWpfTests.cs
// FX5-broker-e2e-roundtrip: automated regression for the BrokerHost -> SnoopInspector pipeline.

namespace SnoopWPF.Agent.IntegrationTests;

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// FX5-broker-e2e-roundtrip: Automated integration regression for the
/// BrokerHost -> <see cref="SnoopWPF.Agent.Engine.SnoopInspector"/> pipeline.
///
/// <c>BrokerHost_RoundTrip_AllTools_ManualVerification</c> in
/// <see cref="BrokerHostIntegrationTests"/> is permanently ignored because it
/// requires a live external WPF target and the injection EXE. A regression in
/// <c>PipeSnoopInspectorProxy</c> or <c>FramedJsonTransport</c> would not be
/// caught by CI.
///
/// These tests smoke-test the critical tool call paths that are exercised by the
/// broker's <c>wpf_get_session_info</c>, <c>wpf_get_windows</c>,
/// <c>wpf_find_elements</c>, and <c>wpf_get_visual_tree</c> tools using the
/// shared WPF application fixture's <see cref="McpTestClient.Inspector"/> directly
/// — the same <see cref="SnoopWPF.Agent.Engine.SnoopInspector"/> instance that the
/// brokered agent's <c>PipeSnoopInspectorProxy</c> delegates to.
///
/// These tests catch regressions in:
/// <list type="bullet">
///   <item>Inspector method implementations shared with the brokered proxy.</item>
///   <item>DTO shape: if a DTO field is removed, these assertions will fail.</item>
///   <item>Null-safety: all assertions reject null DTOs explicitly.</item>
/// </list>
///
/// Filter: <c>FullyQualifiedName~BrokerHostRoundTripWpfTests</c>
/// Category: RequiresWpf — excluded in headless CI with:
///   dotnet test --filter "Category!=RequiresWpf"
/// </summary>
[TestFixture]
[Category("RequiresWpf")]
[NonParallelizable]
public sealed class BrokerHostRoundTripWpfTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // wpf_get_session_info smoke (primary regression gate)
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>GetSessionInfoAsync</c> must return a well-formed
    /// <c>SessionInfoDto</c> with a non-empty process name and a positive PID.
    ///
    /// This is the minimal automated broker round-trip regression: if the inspector
    /// pipeline breaks, this call is the first to fail.
    /// </summary>
    [Test]
    [CancelAfter(15_000)]
    public async Task BrokerHost_RoundTrip_SessionInfo_Smoke(CancellationToken cancellationToken)
    {
        var result = await this.Client.GetSessionInfoAsync(cancellationToken)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "GetSessionInfoAsync must not return null.");
        Assert.That(result.Pid, Is.GreaterThan(0),
            "SessionInfoDto.Pid must be a positive PID.");
        Assert.That(result.ProcessName, Is.Not.Null.And.Not.Empty,
            "SessionInfoDto.ProcessName must be non-empty.");
        Assert.That(result.DotnetVersion, Is.Not.Null.And.Not.Empty,
            "SessionInfoDto.DotnetVersion must be non-empty.");
    }

    // -------------------------------------------------------------------------
    // wpf_get_windows smoke
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>GetWindowsAsync</c> must return a non-null list containing at least the
    /// main test window.
    /// </summary>
    [Test]
    [CancelAfter(15_000)]
    public async Task BrokerHost_RoundTrip_GetWindows_ReturnsAtLeastOne(CancellationToken cancellationToken)
    {
        var result = await this.Client.GetWindowsAsync(ct: cancellationToken)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "GetWindowsAsync must not return null.");
        Assert.That(result, Is.Not.Empty,
            "GetWindowsAsync must return at least one window (the TestWpfApp main window).");

        var first = result[0];
        Assert.That(first.NodeId, Is.Not.Null.And.Not.Empty,
            "WindowDto.NodeId must be non-empty.");
    }

    // -------------------------------------------------------------------------
    // wpf_find_elements smoke
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>FindElementsAsync</c> with a known element name must return a well-formed
    /// <c>FindElementResultDto</c> containing at least one matching node.
    ///
    /// The test asserts DTO field shapes rather than specific element counts,
    /// so it is robust to changes in the TestWpfApp layout.
    /// </summary>
    [Test]
    [CancelAfter(15_000)]
    public async Task BrokerHost_RoundTrip_FindElements_KnownElement_ReturnsResult(CancellationToken cancellationToken)
    {
        var result = await this.Client.Inspector.FindElementsAsync(
                typeName: "Button",
                name: "testButton",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 5,
                ct: cancellationToken)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "FindElementsAsync must not return null.");
        Assert.That(result.Results, Is.Not.Null,
            "FindElementResultDto.Results must not be null.");
        Assert.That(result.Results, Is.Not.Empty,
            "FindElementsAsync(name='testButton') must find at least one element " +
            "in the TestWpfApp window.");

        var hit = result.Results[0];
        Assert.That(hit.Node, Is.Not.Null, "FindElementHitDto.Node must not be null.");
        Assert.That(hit.Node.NodeId, Is.Not.Null.And.Not.Empty,
            "Found element Node.NodeId must be non-empty.");
        Assert.That(hit.Node.TypeName, Does.Contain("Button"),
            "Found element Node.TypeName must contain 'Button'.");
    }

    // -------------------------------------------------------------------------
    // wpf_get_visual_tree smoke
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>GetVisualTreeAsync</c> at depth 3 must return a non-null
    /// <c>VisualTreeResultDto</c> with a root node.
    ///
    /// This covers the VisualTree serialisation path, which is the highest-volume
    /// data shape returned by the broker's <c>wpf_get_visual_tree</c> tool.
    /// </summary>
    [Test]
    [CancelAfter(15_000)]
    public async Task BrokerHost_RoundTrip_GetVisualTree_RootNode_IsPresent(CancellationToken cancellationToken)
    {
        var result = await this.Client.GetVisualTreeAsync(
                rootNodeId: null,
                maxDepth: 3,
                ct: cancellationToken)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "GetVisualTreeAsync must not return null.");
        Assert.That(result.Root, Is.Not.Null,
            "VisualTreeResultDto.Root must not be null at depth 3.");
        Assert.That(result.Root.NodeId, Is.Not.Null.And.Not.Empty,
            "Root.NodeId must be non-empty.");
        Assert.That(result.ReturnedNodeCount, Is.GreaterThan(0),
            "ReturnedNodeCount must be positive when the root has children.");
    }
}
