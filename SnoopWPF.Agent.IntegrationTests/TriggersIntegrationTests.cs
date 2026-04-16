namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for trigger inspection against a real WPF application.
/// Exercises <c>GetTriggersAsync</c> through the <see cref="McpTestClient"/>.
/// </summary>
[TestFixture]
public sealed class TriggersIntegrationTests : WpfIntegrationTestBase
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
    /// Gets the NodeId of the first node whose TypeName matches (case-insensitive suffix).
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
    // GetTriggersAsync — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetTriggersAsync on a Button returns a list (may be empty).
    /// </summary>
    [Test]
    public async Task GetTriggers_OnButton_ReturnsList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("Button not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(triggers, Is.Not.Null,
            "GetTriggersAsync must return a non-null list.");
        // WPF controls typically have style triggers; the list may be non-empty.
    }

    /// <summary>
    /// Verifies that all returned triggers have a non-empty TriggerType.
    /// </summary>
    [Test]
    public async Task GetTriggers_AllTriggers_HaveTriggerType()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("Button not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            Assert.That(trigger.TriggerType, Is.Not.Null.And.Not.Empty,
                "Every trigger must have a non-empty TriggerType.");
        }
    }

    /// <summary>
    /// Verifies that all returned triggers have a non-empty Source field.
    /// </summary>
    [Test]
    public async Task GetTriggers_AllTriggers_HaveSource()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("Button not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            Assert.That(trigger.Source, Is.Not.Null.And.Not.Empty,
                "Every trigger must have a non-empty Source.");
        }
    }

    /// <summary>
    /// Verifies that the Source values are within the expected set.
    /// </summary>
    [Test]
    public async Task GetTriggers_Source_IsOneOfExpectedValues()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("Button not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        var validSources = new[] { "Style", "ControlTemplate", "DataTemplate", "Element" };

        foreach (var trigger in triggers)
        {
            Assert.That(
                validSources,
                Does.Contain(trigger.Source),
                $"Trigger source '{trigger.Source}' is not one of the expected values.");
        }
    }

    /// <summary>
    /// Verifies that GetTriggersAsync on a TextBox returns a list (may be empty).
    /// </summary>
    [Test]
    public async Task GetTriggers_OnTextBox_ReturnsList()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("TextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("TextBox not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(triggers, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that all trigger Conditions and Setters lists are non-null.
    /// </summary>
    [Test]
    public async Task GetTriggers_AllTriggers_ConditionsAndSettersAreNotNull()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Ignore("Button not found in visual tree; test inconclusive.");
            return;
        }

        var triggers = await this.Client.Inspector
            .GetTriggersAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            Assert.That(trigger.Conditions, Is.Not.Null,
                $"Trigger '{trigger.TriggerType}' Conditions must not be null.");
            Assert.That(trigger.Setters, Is.Not.Null,
                $"Trigger '{trigger.TriggerType}' Setters must not be null.");
        }
    }

    /// <summary>
    /// Verifies that GetTriggersAsync throws SnoopException for an unknown node ID.
    /// </summary>
    [Test]
    public void GetTriggers_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .GetTriggersAsync("0:99999999", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }
}
