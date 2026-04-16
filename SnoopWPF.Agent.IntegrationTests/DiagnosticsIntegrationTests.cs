namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Integration tests for diagnostics inspection against a real WPF application.
/// Exercises <c>RunDiagnosticsAsync</c> through the <see cref="McpTestClient"/>.
/// </summary>
[TestFixture]
public sealed class DiagnosticsIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // RunDiagnosticsAsync — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that RunDiagnosticsAsync returns a non-null page (even if empty).
    /// </summary>
    [Test]
    public async Task RunDiagnostics_ReturnsPage()
    {
        var page = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 100,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(page, Is.Not.Null, "RunDiagnosticsAsync must return a non-null page.");
        Assert.That(page.Items, Is.Not.Null, "Items collection must not be null.");
        Assert.That(page.TotalCount, Is.GreaterThanOrEqualTo(0),
            "TotalCount must be non-negative.");
    }

    /// <summary>
    /// Verifies that every returned diagnostic item has a non-empty Name and Level.
    /// </summary>
    [Test]
    public async Task RunDiagnostics_AllItems_HaveNameAndLevel()
    {
        var page = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        foreach (var item in page.Items)
        {
            Assert.That(item.Name, Is.Not.Null,
                "Diagnostic item Name must not be null.");
            Assert.That(item.Level, Is.Not.Null.And.Not.Empty,
                $"Diagnostic item '{item.Name}' must have a non-empty Level.");
        }
    }

    /// <summary>
    /// Verifies that every returned diagnostic item has a non-null Area field.
    /// </summary>
    [Test]
    public async Task RunDiagnostics_AllItems_HaveArea()
    {
        var page = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        foreach (var item in page.Items)
        {
            Assert.That(item.Area, Is.Not.Null,
                $"Diagnostic item '{item.Name}' must have a non-null Area.");
        }
    }

    /// <summary>
    /// Verifies that minLevel filtering returns a subset of or equal items as no-filter.
    /// </summary>
    [Test]
    public async Task RunDiagnostics_MinLevel_FiltersResults()
    {
        var allPage = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 500,
                ct: default)
            .ConfigureAwait(false);

        var filteredPage = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: "Warning",
                cursor: null,
                take: 500,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(filteredPage.TotalCount, Is.LessThanOrEqualTo(allPage.TotalCount),
            "Filtering by minLevel=Warning must return fewer or equal items than no filter.");
    }

    /// <summary>
    /// Verifies that running diagnostics on a specific node (Button) does not throw.
    /// </summary>
    [Test]
    public async Task RunDiagnostics_OnSpecificNode_DoesNotThrow()
    {
        // Get any node ID from the tree.
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 5).ConfigureAwait(false);
        Assert.That(tree.Root, Is.Not.Null);

        var nodeId = tree.Root.NodeId;
        Assert.That(nodeId, Is.Not.Null.And.Not.Empty);

        // Should not throw even if no diagnostics apply.
        var page = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: nodeId,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 100,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(page, Is.Not.Null);
        Assert.That(page.Items, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that TotalCount equals Items.Count when the page is complete (HasMore=false).
    /// </summary>
    [Test]
    public async Task RunDiagnostics_TotalCount_MatchesWhenComplete()
    {
        var page = await this.Client.Inspector
            .RunDiagnosticsAsync(
                nodeId: null,
                providers: null,
                minLevel: null,
                cursor: null,
                take: 1000, // Large enough to get all
                ct: default)
            .ConfigureAwait(false);

        if (!page.HasMore)
        {
            Assert.That(page.Items.Count, Is.EqualTo(page.TotalCount),
                "When HasMore=false, Items.Count must equal TotalCount.");
        }
    }
}
