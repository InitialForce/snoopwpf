namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for behavior inspection against a real WPF application.
/// Exercises <c>GetBehaviorsAsync</c> through the <see cref="McpTestClient"/>.
///
/// The test WPF application does not load System.Windows.Interactivity or
/// Microsoft.Xaml.Behaviors, so the expected result is an empty list.
/// Tests verify that the API handles that case gracefully.
/// </summary>
[TestFixture]
public sealed class BehaviorsIntegrationTests : WpfIntegrationTestBase
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
    /// Gets the NodeId of the first node whose TypeName matches.
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
    // GetBehaviorsAsync — graceful empty result
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetBehaviorsAsync on a Button returns a non-null list.
    /// Since the test app has no behavior assemblies loaded, the result is expected to be empty.
    /// </summary>
    [Test]
    public async Task GetBehaviors_OnButton_ReturnsNonNullList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var behaviors = await this.Client.Inspector
            .GetBehaviorsAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(behaviors, Is.Not.Null,
            "GetBehaviorsAsync must return a non-null list.");
    }

    /// <summary>
    /// Verifies that GetBehaviorsAsync on an element without behavior assemblies returns an empty list.
    /// </summary>
    [Test]
    public async Task GetBehaviors_WithNoBehaviorAssemblies_ReturnsEmptyList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var behaviors = await this.Client.Inspector
            .GetBehaviorsAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        // The test app has no behavior assemblies; expect zero behaviors.
        Assert.That(behaviors, Is.Empty,
            "No behavior assemblies are loaded in the test app; expected an empty list.");
    }

    /// <summary>
    /// Verifies that GetBehaviorsAsync on a TextBox also returns a non-null list.
    /// </summary>
    [Test]
    public async Task GetBehaviors_OnTextBox_ReturnsNonNullList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("TextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("TextBox not found in visual tree; TestWpfApp must contain a TextBox.");
            return;
        }

        var behaviors = await this.Client.Inspector
            .GetBehaviorsAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(behaviors, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that any returned BehaviorDto has non-null TypeName and AssemblyName.
    /// </summary>
    [Test]
    public async Task GetBehaviors_AllBehaviors_HaveTypeAndAssembly()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var behaviors = await this.Client.Inspector
            .GetBehaviorsAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        foreach (var behavior in behaviors)
        {
            Assert.That(behavior.TypeName, Is.Not.Null,
                "BehaviorDto.TypeName must not be null.");
            Assert.That(behavior.AssemblyName, Is.Not.Null,
                "BehaviorDto.AssemblyName must not be null.");
            Assert.That(behavior.Properties, Is.Not.Null,
                "BehaviorDto.Properties must not be null.");
        }
    }

    /// <summary>
    /// Verifies that GetBehaviorsAsync throws SnoopException for an unknown node ID.
    /// </summary>
    [Test]
    public void GetBehaviors_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .GetBehaviorsAsync("0:99999999", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }
}
