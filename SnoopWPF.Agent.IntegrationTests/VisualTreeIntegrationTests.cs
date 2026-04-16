namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests that exercise visual tree traversal against a real WPF application.
/// Tests inspect the tree of the <see cref="TestWpfApp"/> which contains known elements:
/// a Button, TextBox, TextBlock, and ListBox inside a StackPanel.
/// </summary>
[TestFixture]
public sealed class VisualTreeIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flattens a <see cref="NodeDto"/> tree into a flat list for easier assertions.
    /// </summary>
    private static List<NodeDto> Flatten(NodeDto root)
    {
        var result = new List<NodeDto>();
        var queue = new Queue<NodeDto>();
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
    // GetVisualTree
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetVisualTree returns a non-null result with a valid root.
    /// </summary>
    [Test]
    public async Task GetVisualTree_ReturnsRootNode()
    {
        var result = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 10).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Root, Is.Not.Null, "Visual tree must have a root node.");
        Assert.That(result.Root.NodeId, Is.Not.Null.And.Not.Empty,
            "Root node must have a non-empty NodeId.");
    }

    /// <summary>
    /// Verifies that GetVisualTree returns nodes in the tree.
    /// </summary>
    [Test]
    public async Task GetVisualTree_ReturnsNodes()
    {
        var result = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 10).ConfigureAwait(false);

        var nodes = Flatten(result.Root);
        Assert.That(nodes, Is.Not.Empty, "Visual tree should contain at least one node.");
        Assert.That(result.ReturnedNodeCount, Is.GreaterThan(0));
    }

    /// <summary>
    /// Verifies that GetVisualTree respects the maxDepth parameter —
    /// a deeper traversal returns at least as many nodes as a shallow one.
    /// </summary>
    [Test]
    public async Task GetVisualTree_MaxDepth_Respected()
    {
        var shallow = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 1).ConfigureAwait(false);

        var deep = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 10).ConfigureAwait(false);

        Assert.That(deep.ReturnedNodeCount, Is.GreaterThanOrEqualTo(shallow.ReturnedNodeCount),
            "Deeper traversal should return at least as many nodes as shallow traversal.");
    }

    /// <summary>
    /// Verifies that all nodes in the visual tree have non-empty NodeIds.
    /// </summary>
    [Test]
    public async Task GetVisualTree_AllNodes_HaveNodeIds()
    {
        var result = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 5).ConfigureAwait(false);

        var nodes = Flatten(result.Root);
        Assert.That(nodes, Is.Not.Empty);

        foreach (var node in nodes)
        {
            Assert.That(node.NodeId, Is.Not.Null.And.Not.Empty,
                $"Node at type '{node.TypeName}' must have a non-empty NodeId.");
        }
    }

    /// <summary>
    /// Verifies that tree nodes have TypeName populated.
    /// </summary>
    [Test]
    public async Task GetVisualTree_AllNodes_HaveTypeName()
    {
        var result = await this.Client.GetVisualTreeAsync(
            rootNodeId: null,
            maxDepth: 5).ConfigureAwait(false);

        var nodes = Flatten(result.Root);

        foreach (var node in nodes)
        {
            Assert.That(node.TypeName, Is.Not.Null.And.Not.Empty,
                "TypeName must be populated for every node.");
        }
    }

    // -------------------------------------------------------------------------
    // GetChildren
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetChildren returns children for the root.
    /// </summary>
    [Test]
    public async Task GetChildren_Root_ReturnsChildren()
    {
        var page = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);

        Assert.That(page.Items, Is.Not.Empty,
            "Root-level children should not be empty.");
        Assert.That(page.TotalCount, Is.GreaterThan(0));
    }

    /// <summary>
    /// Verifies that a second call to GetChildren for the same node produces stable node IDs.
    /// </summary>
    [Test]
    public async Task GetChildren_NodeIds_AreStableAcrossCalls()
    {
        var page1 = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);
        var page2 = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);

        Assert.That(page1.Items.Count, Is.EqualTo(page2.Items.Count),
            "Child count must be stable between consecutive calls.");

        for (var i = 0; i < page1.Items.Count; i++)
        {
            Assert.That(page1.Items[i].NodeId, Is.EqualTo(page2.Items[i].NodeId),
                $"Node ID at position {i} must be stable.");
        }
    }

    /// <summary>
    /// Verifies that GetChildren can navigate into a child node that itself has children.
    /// </summary>
    [Test]
    public async Task GetChildren_WithNodeId_ReturnsChildrenOfThatNode()
    {
        // Get the root children first
        var rootPage = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);
        Assert.That(rootPage.Items, Is.Not.Empty, "Root must have children.");

        // Navigate into the first child that itself has children
        var parentNode = rootPage.Items.FirstOrDefault(n => n.ChildCount > 0);

        if (parentNode == null)
        {
            Assert.Ignore("No child node with children found at root level; test inconclusive.");
            return;
        }

        var childPage = await this.Client.GetChildrenAsync(
            nodeId: parentNode.NodeId).ConfigureAwait(false);

        Assert.That(childPage.Items, Is.Not.Empty,
            $"Node '{parentNode.TypeName}' with ChildCount={parentNode.ChildCount} should have children.");
    }

    /// <summary>
    /// Verifies that requesting children for an unknown node ID throws SnoopException
    /// with NodeNotFound error code.
    /// </summary>
    [Test]
    public void GetChildren_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.GetChildrenAsync(nodeId: "0:99999999").ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }

    // -------------------------------------------------------------------------
    // Pagination
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that TotalCount in a page is non-negative.
    /// </summary>
    [Test]
    public async Task GetChildren_TotalCount_IsNonNegative()
    {
        var page = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);

        Assert.That(page.TotalCount, Is.GreaterThanOrEqualTo(0));
    }

    /// <summary>
    /// Verifies that NextCursor is null when HasMore is false.
    /// </summary>
    [Test]
    public async Task GetChildren_WhenHasMoreFalse_NextCursorIsNull()
    {
        // Request a large page — the test window should fit comfortably
        var page = await this.Client.GetChildrenAsync(nodeId: null, take: 500).ConfigureAwait(false);

        if (!page.HasMore)
        {
            Assert.That(page.NextCursor, Is.Null,
                "NextCursor must be null when HasMore=false.");
        }
    }
}
