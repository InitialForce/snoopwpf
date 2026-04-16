namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for ancestor chain inspection against a real WPF application.
/// Exercises <c>GetAncestorsAsync</c> through the <see cref="McpTestClient"/>.
///
/// The <see cref="TestWpfApp"/> hierarchy is:
///   Window → StackPanel → Button / TextBox / TextBlock / ListBox
/// </summary>
[TestFixture]
public sealed class AncestorsIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flattens a <see cref="NodeDto"/> tree into a flat list.
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

    /// <summary>
    /// Gets the NodeId of the first node whose TypeName matches (case-insensitive).
    /// </summary>
    private async Task<string?> FindNodeIdByTypeAsync(string typeName)
    {
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 10).ConfigureAwait(false);
        var nodes = Flatten(tree.Root);
        var node = nodes.FirstOrDefault(
            n => n.TypeName.Equals(typeName, System.StringComparison.OrdinalIgnoreCase)
              || n.TypeName.EndsWith("." + typeName, System.StringComparison.OrdinalIgnoreCase));
        return node?.NodeId;
    }

    // -------------------------------------------------------------------------
    // GetAncestorsAsync — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetAncestorsAsync on a Button returns a non-empty ancestor list.
    /// The Button is nested inside a StackPanel inside a Window, so we expect at least
    /// 2 ancestors (StackPanel + Window plus possible rendering ancestors).
    /// </summary>
    [Test]
    public async Task GetAncestors_OnButton_ReturnsNonEmptyList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ancestors = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: null, ct: default)
            .ConfigureAwait(false);

        Assert.That(ancestors, Is.Not.Null, "GetAncestorsAsync must return a non-null list.");
        Assert.That(ancestors.Count, Is.GreaterThan(0),
            "Button has at least one ancestor (StackPanel).");
    }

    /// <summary>
    /// Verifies that every ancestor has a non-empty NodeId and TypeName.
    /// </summary>
    [Test]
    public async Task GetAncestors_AllAncestors_HaveNodeIdAndTypeName()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ancestors = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: null, ct: default)
            .ConfigureAwait(false);

        foreach (var ancestor in ancestors)
        {
            Assert.That(ancestor.NodeId, Is.Not.Null.And.Not.Empty,
                "AncestorDto.NodeId must not be empty.");
            Assert.That(ancestor.TypeName, Is.Not.Null.And.Not.Empty,
                "AncestorDto.TypeName must not be empty.");
        }
    }

    /// <summary>
    /// Verifies that the ancestor chain includes a Window somewhere (the test window is at the root).
    /// </summary>
    [Test]
    public async Task GetAncestors_Button_ChainIncludesWindow()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ancestors = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: null, ct: default)
            .ConfigureAwait(false);

        var hasWindow = ancestors.Any(
            a => a.TypeName.Equals("Window", System.StringComparison.OrdinalIgnoreCase)
              || a.TypeName.EndsWith(".Window", System.StringComparison.OrdinalIgnoreCase)
              || a.TypeName.Contains("Window", System.StringComparison.OrdinalIgnoreCase));

        Assert.That(hasWindow, Is.True,
            "Ancestor chain for Button must include a Window-derived type.");
    }

    // -------------------------------------------------------------------------
    // GetAncestorsAsync — maxLevels
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that maxLevels=1 returns at most 1 ancestor.
    /// </summary>
    [Test]
    public async Task GetAncestors_MaxLevels1_ReturnsAtMostOne()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ancestors = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: 1, ct: default)
            .ConfigureAwait(false);

        Assert.That(ancestors.Count, Is.LessThanOrEqualTo(1),
            "maxLevels=1 must return at most 1 ancestor.");
    }

    /// <summary>
    /// Verifies that increasing maxLevels returns at least as many ancestors.
    /// </summary>
    [Test]
    public async Task GetAncestors_HigherMaxLevels_ReturnsMoreOrEqualAncestors()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var narrow = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: 1, ct: default)
            .ConfigureAwait(false);

        var broad = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: 50, ct: default)
            .ConfigureAwait(false);

        Assert.That(broad.Count, Is.GreaterThanOrEqualTo(narrow.Count),
            "Higher maxLevels must return at least as many ancestors.");
    }

    // -------------------------------------------------------------------------
    // GetAncestorsAsync — TextBox
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetAncestorsAsync on a TextBox also returns ancestors.
    /// </summary>
    [Test]
    public async Task GetAncestors_OnTextBox_ReturnsAncestors()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("TextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("TextBox not found in visual tree; TestWpfApp must contain a TextBox.");
            return;
        }

        var ancestors = await this.Client.Inspector
            .GetAncestorsAsync(nodeId, maxLevels: null, ct: default)
            .ConfigureAwait(false);

        Assert.That(ancestors, Is.Not.Null);
        Assert.That(ancestors.Count, Is.GreaterThan(0),
            "TextBox has at least one ancestor (StackPanel).");
    }

    // -------------------------------------------------------------------------
    // GetAncestorsAsync — error
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetAncestorsAsync throws SnoopException for an unknown node ID.
    /// </summary>
    [Test]
    public void GetAncestors_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .GetAncestorsAsync("0:99999999", maxLevels: null, ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }
}
