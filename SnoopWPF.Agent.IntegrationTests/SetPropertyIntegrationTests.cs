namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for property mutation against a real WPF application.
/// Exercises <c>SetPropertyAsync</c> through the <see cref="McpTestClient"/>.
///
/// The shared <see cref="McpTestClient"/> has <c>EnableMutation=true</c> and
/// <c>EnableRedaction=false</c>, which means mutation tests can run.
/// Tests that verify the disabled-mutation guard use a separate inspector instance.
/// </summary>
[TestFixture]
public sealed class SetPropertyIntegrationTests : WpfIntegrationTestBase
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
    // SetPropertyAsync — Width
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that setting the Width on a Button returns success=true and correct values.
    /// </summary>
    [Test]
    public async Task SetProperty_Width_OnButton_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        // Set Width to 150 (different from the TestWpfApp default of 120).
        var result = await this.Client.Inspector
            .SetPropertyAsync(nodeId, "Width", "150", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "SetPropertyAsync must return a non-null result.");
        Assert.That(result.Success, Is.True, "Setting Width=150 on Button must succeed.");
        Assert.That(result.NewValue, Is.Not.Null,
            "NewValue must be populated after a successful set.");
        Assert.That(result.Error, Is.Null,
            "Error must be null when the operation succeeds.");
    }

    /// <summary>
    /// Verifies that SetPropertyAsync returns the previous value before the change.
    /// </summary>
    [Test]
    public async Task SetProperty_Width_ReturnsPreviousValue()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        // First read current width via GetProperties.
        var propsPage = await this.Client.GetPropertiesAsync(nodeId: nodeId, filter: "Width", take: 50)
            .ConfigureAwait(false);
        var widthProp = propsPage.Items.FirstOrDefault(
            p => p.Name.Equals("Width", System.StringComparison.OrdinalIgnoreCase));

        // Change width.
        var result = await this.Client.Inspector
            .SetPropertyAsync(nodeId, "Width", "180", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.PreviousValue, Is.Not.Null,
            "PreviousValue must not be null.");

        // If we found the width property, the PreviousValue should match.
        if (widthProp != null && !string.IsNullOrEmpty(widthProp.Value))
        {
            Assert.That(result.PreviousValue, Is.EqualTo(widthProp.Value),
                "PreviousValue must match the value read before the change.");
        }
    }

    // -------------------------------------------------------------------------
    // SetPropertyAsync — mutation disabled
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that SetPropertyAsync throws MutationDisabled when EnableMutation=false.
    /// Uses a separate inspector instance with mutation disabled.
    /// </summary>
    [Test]
    public async Task SetProperty_MutationDisabled_ThrowsMutationDisabledError()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        // Create a separate inspector with mutation disabled.
        using var readOnlyInspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await readOnlyInspector
                .SetPropertyAsync(nodeId, "Width", "100", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.MutationDisabled),
            "Setting a property with mutation disabled must throw MutationDisabled.");
    }

    // -------------------------------------------------------------------------
    // SetPropertyAsync — unknown node
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that SetPropertyAsync throws SnoopException for an unknown node ID.
    /// </summary>
    [Test]
    public void SetProperty_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .SetPropertyAsync("0:99999999", "Width", "100", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }

    // -------------------------------------------------------------------------
    // SetPropertyAsync — read-only / unsupported property
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that attempting to set a read-only property throws an appropriate SnoopException.
    /// ActualWidth is a read-only DependencyProperty on FrameworkElement.
    /// </summary>
    [Test]
    public async Task SetProperty_ReadOnlyProperty_ThrowsSnoopException()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .SetPropertyAsync(nodeId, "ActualWidth", "200", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        // ActualWidth is read-only — expect PropertyReadOnly or UnsupportedPropertyType.
        Assert.That(
            ex!.Code,
            Is.AnyOf(
                SnoopWPF.Agent.Contracts.SnoopErrorCode.PropertyReadOnly,
                SnoopWPF.Agent.Contracts.SnoopErrorCode.UnsupportedPropertyType),
            "Setting a read-only property must throw PropertyReadOnly or UnsupportedPropertyType.");
    }

    /// <summary>
    /// Verifies that attempting to set a non-existent property name throws an appropriate SnoopException.
    /// </summary>
    [Test]
    public async Task SetProperty_NonExistentProperty_ThrowsSnoopException()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.Inspector
                .SetPropertyAsync(nodeId, "PropertyThatDoesNotExistAtAll_xyz_99", "value", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(
            ex!.Code,
            Is.AnyOf(
                SnoopWPF.Agent.Contracts.SnoopErrorCode.PropertyReadOnly,
                SnoopWPF.Agent.Contracts.SnoopErrorCode.UnsupportedPropertyType),
            "Setting a non-existent property must throw PropertyReadOnly or UnsupportedPropertyType.");
    }
}
