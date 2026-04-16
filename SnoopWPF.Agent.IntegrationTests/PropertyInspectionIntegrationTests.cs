namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for property inspection against a real WPF application.
/// The <see cref="TestWpfApp"/> contains a Button ("testButton") and TextBox ("testTextBox")
/// with known properties.
/// </summary>
[TestFixture]
public sealed class PropertyInspectionIntegrationTests : WpfIntegrationTestBase
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
    /// Gets the NodeId of the first node that has a TypeName matching <paramref name="typeName"/>.
    /// Returns null if no such node is found.
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
    // GetProperties — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetProperties on a Button node returns a non-empty property list.
    /// </summary>
    [Test]
    public async Task GetProperties_OnButton_ReturnsProperties()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var page = await this.Client.GetPropertiesAsync(nodeId: nodeId).ConfigureAwait(false);

        Assert.That(page.Items, Is.Not.Empty,
            "Button should expose at least some properties.");
        Assert.That(page.TotalCount, Is.GreaterThan(0));
    }

    /// <summary>
    /// Verifies that Width and Height appear in a Button's property list.
    /// </summary>
    [Test]
    public async Task GetProperties_Button_ContainsWidthAndHeight()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        // Retrieve all properties (use a large page size)
        var page = await this.Client.GetPropertiesAsync(nodeId: nodeId, take: 500)
            .ConfigureAwait(false);

        var names = page.Items.Select(p => p.Name).ToList();

        Assert.That(names, Does.Contain("Width"),
            "Button should have a 'Width' property.");
        Assert.That(names, Does.Contain("Height"),
            "Button should have a 'Height' property.");
    }

    /// <summary>
    /// Verifies that the filter parameter narrows results by property name.
    /// </summary>
    [Test]
    public async Task GetProperties_WithFilter_NarrowsResults()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var unfiltered = await this.Client.GetPropertiesAsync(nodeId: nodeId, take: 500)
            .ConfigureAwait(false);

        var filtered = await this.Client.GetPropertiesAsync(nodeId: nodeId, filter: "Width", take: 500)
            .ConfigureAwait(false);

        Assert.That(filtered.Items.Count, Is.LessThanOrEqualTo(unfiltered.Items.Count),
            "Filtered result must be a subset of unfiltered.");

        Assert.That(filtered.Items, Is.Not.Empty,
            "Filter 'Width' should match at least one property.");

        foreach (var prop in filtered.Items)
        {
            Assert.That(
                prop.Name.IndexOf("Width", System.StringComparison.OrdinalIgnoreCase),
                Is.GreaterThanOrEqualTo(0),
                $"Filtered property '{prop.Name}' must contain 'Width'.");
        }
    }

    /// <summary>
    /// Verifies that every returned PropertyDto has a non-empty Name.
    /// </summary>
    [Test]
    public async Task GetProperties_AllProperties_HaveNames()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var page = await this.Client.GetPropertiesAsync(nodeId: nodeId, take: 500)
            .ConfigureAwait(false);

        foreach (var prop in page.Items)
        {
            Assert.That(prop.Name, Is.Not.Null.And.Not.Empty,
                "Every property must have a non-empty Name.");
        }
    }

    // -------------------------------------------------------------------------
    // GetProperties — TextBox
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that a TextBox has a 'Text' property with the expected value.
    /// </summary>
    [Test]
    public async Task GetProperties_TextBox_HasTextProperty()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("TextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("TextBox not found in visual tree; TestWpfApp must contain a TextBox.");
            return;
        }

        var page = await this.Client.GetPropertiesAsync(nodeId: nodeId, filter: "Text", take: 100)
            .ConfigureAwait(false);

        var textProp = page.Items.FirstOrDefault(
            p => p.Name.Equals("Text", System.StringComparison.OrdinalIgnoreCase));

        Assert.That(textProp, Is.Not.Null,
            "TextBox should have a 'Text' property.");

        // The TestWpfApp sets Text = "Hello WPF"
        Assert.That(textProp!.Value, Is.EqualTo("Hello WPF"),
            "TextBox.Text should equal the value set in TestWpfApp.");
    }

    // -------------------------------------------------------------------------
    // GetBindingInfo
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetBindingInfo on a non-bound property returns HasBinding=false.
    /// </summary>
    [Test]
    public async Task GetBindingInfo_UnboundProperty_ReturnsHasBindingFalse()
    {
        var nodeId = await this.FindNodeIdByTypeAsync("Button").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("Button not found in visual tree; TestWpfApp must contain a Button.");
            return;
        }

        var binding = await this.Client.GetBindingInfoAsync(nodeId, "Width")
            .ConfigureAwait(false);

        Assert.That(binding, Is.Not.Null);
        // Width is set directly (not via binding) so HasBinding should be false
        Assert.That(binding.HasBinding, Is.False,
            "Width on a simple Button should not have a data binding.");
    }

    // -------------------------------------------------------------------------
    // Error scenarios
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that requesting properties for an unknown nodeId throws SnoopException.
    /// </summary>
    [Test]
    public void GetProperties_UnknownNodeId_ThrowsSnoopException()
    {
        var ex = Assert.ThrowsAsync<SnoopWPF.Agent.Contracts.SnoopException>(async () =>
        {
            await this.Client.GetPropertiesAsync(nodeId: "0:99999999").ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.NodeNotFound));
    }
}
