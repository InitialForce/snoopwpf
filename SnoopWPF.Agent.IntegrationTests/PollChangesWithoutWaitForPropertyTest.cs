namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Spike S-3b — Circular-dependency test for poll_changes (M0-04, bd-9pl).
///
/// Proves that a structural mutation (Button removal) can be detected via a
/// tree-walk delta WITHOUT relying on the property-polling tool family.
/// Breaking this cycle prevents a class of false-positive test passes where a
/// bug in the polling infrastructure causes multiple polling tools to hang
/// together, hiding the root fault.
///
/// Detection strategy (no wpf_poll_changes MCP tool yet — see LIMITATION below):
///   1. Walk the full visual tree and collect all node IDs (version sentinel = node count).
///   2. Remove the Button on the Dispatcher thread.
///   3. <c>Thread.Sleep(50)</c> — the only timing primitive used; no polling.
///   4. Walk the visual tree again and recount.
///   5. Assert: treeVersionDelta = nodeCountBefore − nodeCountAfter >= 1.
///   6. Assert: the removed Button's nodeId is absent from the after-tree.
///
/// LIMITATION: This test exercises the same structural-change detection logic by calling
/// <see cref="SnoopWPF.Agent.Engine.SnoopInspector"/> APIs directly rather than
/// through the MCP tool surface.  The production form of this test lives in
/// <see cref="PollChangesIntegrationTests"/> which calls <c>PollChangesAsync</c> directly
/// and asserts on the returned <c>treeVersionDelta</c> and <c>changeSet</c> fields.
///
/// Anti-pattern constraint: MUST NOT contain any call to the property-polling
/// tool family.  The acceptance-criteria grep enforces this on file text.
/// </summary>
[TestFixture]
public sealed class PollChangesWithoutWaitForPropertyTest : WpfIntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recursively flattens a <see cref="NodeDto"/> tree into a flat list of nodeIds.
    /// </summary>
    private static HashSet<string> CollectNodeIds(NodeDto root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<NodeDto>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!string.IsNullOrEmpty(node.NodeId))
            {
                result.Add(node.NodeId);
            }

            if (node.Children is not null)
            {
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Test
    // -----------------------------------------------------------------------

    /// <summary>
    /// Removes the "testButton" from the live visual tree and verifies the
    /// removal is reflected in a subsequent tree walk — using only
    /// <c>Thread.Sleep</c>, not any form of property polling.
    /// </summary>
    [Test]
    public async Task RemoveButton_VisualTreeWalk_ReflectsMutation_WithoutPolling()
    {
        // ------------------------------------------------------------------
        // Step 1: find the Button and capture its nodeId via find_elements.
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
        // Step 2: Synthesise a version sentinel.
        // Walk the visual tree and count all nodes; the count acts as a
        // coarse treeVersion proxy until M1-08 adds a dedicated counter to
        // wpf_get_session_info.
        // ------------------------------------------------------------------
        var treeBefore = await this.Client.Inspector
            .GetVisualTreeAsync(
                rootNodeId: null,
                maxDepth: 10,
                treeType: "visual",
                includeProperties: null,
                ct: default)
            .ConfigureAwait(false);

        var nodeIdsBefore = CollectNodeIds(treeBefore.Root);
        var versionBefore = nodeIdsBefore.Count;

        Assert.That(nodeIdsBefore, Does.Contain(buttonNodeId),
            "The Button's nodeId must appear in the before-snapshot.");

        // ------------------------------------------------------------------
        // Step 3: On the Dispatcher thread, remove the Button from the
        // rootPanel StackPanel.
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
        // Step 4: Thread.Sleep(50) — the ONLY timing primitive.
        //         This is intentional: no property-polling tool, no polling loop.
        // ------------------------------------------------------------------
        Thread.Sleep(50);

        // ------------------------------------------------------------------
        // Step 5: Walk the visual tree again (poll_changes proxy call).
        //         In production this will be replaced by:
        //           PollChangesAsync(sinceVersion: before)
        //         but the same internal detection logic is exercised here.
        // ------------------------------------------------------------------
        var treeAfter = await this.Client.Inspector
            .GetVisualTreeAsync(
                rootNodeId: null,
                maxDepth: 10,
                treeType: "visual",
                includeProperties: null,
                ct: default)
            .ConfigureAwait(false);

        var nodeIdsAfter = CollectNodeIds(treeAfter.Root);
        var versionAfter = nodeIdsAfter.Count;

        // ------------------------------------------------------------------
        // Step 6: Assert treeVersionDelta >= 1.
        // ------------------------------------------------------------------
        var treeVersionDelta = versionBefore - versionAfter;

        Assert.That(treeVersionDelta, Is.GreaterThanOrEqualTo(1),
            $"Removing a Button must decrease the registered node count by at least 1. " +
            $"Before={versionBefore}, After={versionAfter}, Delta={treeVersionDelta}.");

        // Assert: the removed nodeId is absent from the after-tree.
        Assert.That(nodeIdsAfter, Does.Not.Contain(buttonNodeId),
            $"The removed Button's nodeId ({buttonNodeId}) must not appear in the after-snapshot.");
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
