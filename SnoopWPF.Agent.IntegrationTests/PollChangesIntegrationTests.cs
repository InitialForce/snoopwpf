namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

/// <summary>
/// Production integration tests for <c>wpf_poll_changes</c> (M2-10, bd-191).
///
/// These are the production-form equivalents of the S-3b spike in
/// <see cref="PollChangesWithoutWaitForPropertyTest"/> (M0-04, bd-9pl).
/// Where the spike used direct visual-tree walks as a proxy, these tests
/// call <c>PollChangesAsync</c> directly and assert on the returned
/// <c>changeSet</c> and <c>treeVersion</c> fields.
///
/// Anti-pattern constraint: MUST NOT contain any call to the
/// property-polling tool family (WaitForProperty / wpf_wait_for_property).
/// </summary>
[TestFixture]
public sealed class PollChangesIntegrationTests : WpfIntegrationTestBase
{
    // -----------------------------------------------------------------------
    // S-3b (Production) — Structural-change detection via MCP tool surface
    // -----------------------------------------------------------------------

    /// <summary>
    /// Removes the "testButton" from the live visual tree and verifies that
    /// <c>PollChangesAsync</c> reports the removed nodeId in its changeset.
    ///
    /// This is the production replacement for
    /// <see cref="PollChangesWithoutWaitForPropertyTest.RemoveButton_VisualTreeWalk_ReflectsMutation_WithoutPolling"/>
    /// which used a manual tree-walk proxy instead of the MCP tool surface.
    /// </summary>
    [Test]
    public async Task PollChanges_AfterButtonRemoval_ReportsRemovedNode()
    {
        // ------------------------------------------------------------------
        // Step 1: capture baseline treeVersion + buttonNodeId via find.
        // ------------------------------------------------------------------
        var findResult = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: "testButton",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 5,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(findResult.Results, Is.Not.Empty,
            "testButton must be present in the visual tree before the test runs.");

        var buttonNodeId = findResult.Results.First().Node.NodeId;
        Assert.That(buttonNodeId, Is.Not.Null.And.Not.Empty,
            "The found Button must have a non-empty nodeId.");

        // ------------------------------------------------------------------
        // Step 2: pre-register nodes via GetVisualTree, then capture baseline.
        // PollChangesAsync uses GetExistingId (non-registering), so nodes must
        // be registered first.
        // ------------------------------------------------------------------
        await this.Client.GetVisualTreeAsync(maxDepth: 10).ConfigureAwait(false);

        var baseline = await this.Client.PollChangesAsync(sinceVersion: 0)
            .ConfigureAwait(false);

        Assert.That(baseline.TreeVersion, Is.GreaterThan(0),
            "TreeVersion must be positive after initial registration of nodes.");

        var sinceVersion = baseline.TreeVersion;

        // Verify buttonNodeId appears as "added" in the baseline (sinceVersion=0)
        // or is already present — the key thing is the button is known.
        // We just need sinceVersion for the delta poll.

        // ------------------------------------------------------------------
        // Step 3: Remove the Button on the Dispatcher thread.
        // ------------------------------------------------------------------
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;
            var button = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b => b.Name == "testButton");

            Assert.That(button, Is.Not.Null,
                "testButton must still be in rootPanel.Children when the Dispatcher executes.");

            rootPanel.Children.Remove(button);
        });

        // ------------------------------------------------------------------
        // Step 4: Thread.Sleep(50) — same timing constraint as S-3b spike.
        //         This is intentional: no property-polling tool.
        // ------------------------------------------------------------------
        Thread.Sleep(50);

        // ------------------------------------------------------------------
        // Step 5: Call PollChangesAsync with the baseline treeVersion.
        // ------------------------------------------------------------------
        var delta = await this.Client.PollChangesAsync(sinceVersion: sinceVersion)
            .ConfigureAwait(false);

        Assert.That(delta.TreeVersion, Is.GreaterThanOrEqualTo(sinceVersion),
            "TreeVersion must be >= sinceVersion after the poll.");

        // ------------------------------------------------------------------
        // Step 6: Assert the removed Button's nodeId is in the changeset.
        // ------------------------------------------------------------------
        var removedEntries = delta.Changes
            .Where(c => c.ChangeKind == "removed")
            .Select(c => c.NodeId)
            .ToList();

        Assert.That(removedEntries, Does.Contain(buttonNodeId),
            $"The removed Button's nodeId ({buttonNodeId}) must appear in the 'removed' changeset. " +
            $"Actual changeset: {string.Join(", ", delta.Changes.Select(c => $"{c.ChangeKind}:{c.NodeId}"))}");
    }

    /// <summary>
    /// Verifies that <c>PollChangesAsync(sinceVersion=0)</c> returns a non-empty
    /// set of "added" entries and a positive treeVersion after nodes have been
    /// registered via GetVisualTree.
    /// </summary>
    [Test]
    public async Task PollChanges_InitialPoll_ReturnsAddedNodes()
    {
        // Pre-register nodes by walking the visual tree.
        // PollChangesAsync uses GetExistingId (non-registering walk), so nodes must
        // be registered by a prior operation to appear in the changeset.
        await this.Client.GetVisualTreeAsync(maxDepth: 5).ConfigureAwait(false);

        var result = await this.Client.PollChangesAsync(sinceVersion: 0)
            .ConfigureAwait(false);

        Assert.That(result.TreeVersion, Is.GreaterThan(0),
            "Initial poll must return a positive treeVersion.");

        Assert.That(result.Changes, Is.Not.Empty,
            "Initial poll (sinceVersion=0) must return at least one 'added' entry.");

        var addedEntries = result.Changes.Where(c => c.ChangeKind == "added").ToList();
        Assert.That(addedEntries, Is.Not.Empty,
            "At least one node must appear as 'added' in the initial poll.");

        Assert.That(result.ChangeCount, Is.EqualTo(result.Changes.Count),
            "ChangeCount must match Changes.Count.");
    }

    /// <summary>
    /// Verifies that a second poll with the same treeVersion returned by the first
    /// poll reports an empty changeset (no spurious mutations).
    /// </summary>
    [Test]
    public async Task PollChanges_ImmediateRepeat_ReportsNoChanges()
    {
        // Pre-register nodes so PollChangesAsync has something to work with.
        await this.Client.GetVisualTreeAsync(maxDepth: 5).ConfigureAwait(false);

        // First poll to get a stable baseline.
        var first = await this.Client.PollChangesAsync(sinceVersion: 0)
            .ConfigureAwait(false);

        Assert.That(first.TreeVersion, Is.GreaterThan(0));

        // Second poll — no mutations in between.
        var second = await this.Client.PollChangesAsync(sinceVersion: first.TreeVersion)
            .ConfigureAwait(false);

        Assert.That(second.Changes, Is.Empty,
            "A second immediate poll should report no changes when the tree has not mutated.");

        Assert.That(second.ChangeCount, Is.EqualTo(0),
            "ChangeCount must be 0 when Changes is empty.");
    }

    // -----------------------------------------------------------------------
    // Teardown — restore the Button so other tests are not affected.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Re-adds "testButton" to rootPanel so the shared WPF app is left in its
    /// original state for subsequent test fixtures.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;

            // Only restore if it is actually missing.
            var existing = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b => b.Name == "testButton");

            if (existing is not null)
            {
                return;
            }

            var testButton = new Button
            {
                Name = "testButton",
                Content = "Click Me",
                Width = 120,
                Height = 32,
            };

            // Insert at index 0 to match the original order.
            rootPanel.Children.Insert(0, testButton);
        });
    }
}
