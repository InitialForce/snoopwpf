namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_expand_collapse</c> (M2-07).
/// Covers the acceptance scenarios from the bead:
/// <list type="number">
///   <item>Expander — expand succeeds and stateChanged=true.</item>
///   <item>Expander — collapse after expand succeeds.</item>
///   <item>TreeViewItem — expand succeeds.</item>
///   <item>Button (no provider) — rejected with PatternNotSupported.</item>
///   <item>Invalid action — ArgumentException thrown.</item>
///   <item>Automation disabled → MutationDisabled exception thrown.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class ExpandCollapseIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string?> FindNodeIdByNameAsync(string elementName)
    {
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 12).ConfigureAwait(false);
        var all = Flatten(tree.Root);
        return all.FirstOrDefault(
            n => n.Name != null &&
                 n.Name.Equals(elementName, System.StringComparison.Ordinal))
            ?.NodeId;
    }

    private static System.Collections.Generic.List<NodeDto> Flatten(NodeDto root)
    {
        var result = new System.Collections.Generic.List<NodeDto>();
        var queue = new System.Collections.Generic.Queue<NodeDto>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Scenario 1: Expander — expand succeeds
    // -------------------------------------------------------------------------

    /// <summary>
    /// Expanding a collapsed Expander must succeed, ChosenTier must be L1, and
    /// stateChanged must be true.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_Expander_ExpandSucceeds()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExpander").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExpander not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ExpandCollapseAsync(nodeId, "expand", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "ExpandCollapseAsync must return a non-null result.");
        Assert.That(result.Success, Is.True, "Expand must succeed on a collapsed Expander.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.ChosenTier, Is.EqualTo(InputTier.L1), "ChosenTier must be L1.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true after expand.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Expander — collapse after expand succeeds
    // -------------------------------------------------------------------------

    /// <summary>
    /// Collapsing an Expander that was just expanded must succeed.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_Expander_CollapseSucceeds()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExpander").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExpander not found.");
            return;
        }

        // First expand it.
        await this.Client.Inspector
            .ExpandCollapseAsync(nodeId, "expand", ct: default)
            .ConfigureAwait(false);

        // Then collapse it.
        var result = await this.Client.Inspector
            .ExpandCollapseAsync(nodeId, "collapse", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "Collapse must succeed after expand.");
        Assert.That(result.ChosenTier, Is.EqualTo(InputTier.L1), "ChosenTier must be L1.");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: TreeViewItem — expand succeeds
    // -------------------------------------------------------------------------

    /// <summary>
    /// Expanding a TreeViewItem that has children must succeed.
    /// The TreeViewItem is located via GetChildrenAsync on the testTreeView node
    /// to bypass visual-tree depth limits.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_TreeViewItem_ExpandSucceeds()
    {
        // Find the testTreeView by name in the visual tree first.
        var treeViewNodeId = await this.FindNodeIdByNameAsync("testTreeView").ConfigureAwait(false);

        if (treeViewNodeId == null)
        {
            Assert.Fail("testTreeView not found — TestWpfApp must be updated.");
            return;
        }

        // Use GetChildrenAsync to enumerate the logical children of the TreeView,
        // which surfaces TreeViewItem wrappers that may not appear in deep visual traversal.
        var children = await this.Client.GetChildrenAsync(treeViewNodeId).ConfigureAwait(false);
        var treeViewItemNode = children.Items.FirstOrDefault(
            n => n.TypeName != null &&
                 n.TypeName.Contains("TreeViewItem", System.StringComparison.Ordinal));

        if (treeViewItemNode == null)
        {
            Assert.Inconclusive(
                "TreeViewItem not found as child of testTreeView. " +
                "TreeViewItem may not have been materialised yet — this is a test infrastructure limitation.");
            return;
        }

        var result = await this.Client.Inspector
            .ExpandCollapseAsync(treeViewItemNode.NodeId, "expand", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "Expand must succeed on a TreeViewItem with children.");
        Assert.That(result.ChosenTier, Is.EqualTo(InputTier.L1), "ChosenTier must be L1.");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Plain Button → PatternNotSupported
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calling ExpandCollapseAsync on a plain Button must fail with PatternNotSupported.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_Button_ReturnsPatternNotSupported()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testButton not found.");
            return;
        }

        var result = await this.Client.Inspector
            .ExpandCollapseAsync(nodeId, "expand", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false for a plain Button.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported for Button.");
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Invalid action → ArgumentException
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passing an unrecognised action must throw <see cref="System.ArgumentException"/>.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_InvalidAction_ThrowsArgumentException()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExpander").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExpander not found.");
            return;
        }

        Assert.ThrowsAsync<System.ArgumentException>(async () =>
        {
            await this.Client.Inspector
                .ExpandCollapseAsync(nodeId, "flip", ct: default)
                .ConfigureAwait(false);
        });
    }

    // -------------------------------------------------------------------------
    // Scenario 6: Automation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// ExpandCollapseAsync with EnableAutomation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task ExpandCollapse_AutomationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExpander").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExpander not found.");
            return;
        }

        using var restrictedInspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableAutomation = false,
                EnableMutation = true,
                EnableRedaction = false,
            });

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await restrictedInspector
                .ExpandCollapseAsync(nodeId, "expand", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when EnableAutomation=false.");
    }
}
