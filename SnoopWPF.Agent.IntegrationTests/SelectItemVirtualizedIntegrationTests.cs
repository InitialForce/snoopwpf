namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_select_item</c> (M2-04b, L0 virtualised scroll path).
/// Drives the 10 000-item <c>testBigList</c> <see cref="System.Windows.Controls.ListBox"/>
/// that is backed by a <see cref="System.Windows.Controls.VirtualizingStackPanel"/>
/// (added in M1-06). Items are formatted as <c>"Item {i}"</c> (zero-based).
///
/// Acceptance criteria from bead bd-2cd:
/// <list type="number">
///   <item>Select item near the start (in-view, already materialised) → success.</item>
///   <item>Select item near the end (off-screen, requires scroll-materialise) → success.</item>
///   <item>Select item by partial text in a large list → success.</item>
///   <item>Select item by numeric index in a large list → success.</item>
///   <item>Ambiguous partial text in a large list → LocatorAmbiguous.</item>
///   <item>Out-of-range index → ElementNotFound.</item>
///   <item>stateChanged=false when the same item is re-selected.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class SelectItemVirtualizedIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string?> FindNodeIdByNameAsync(string elementName)
    {
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 10).ConfigureAwait(false);
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
    // Virtualized list — first item (already materialised on initial render)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting the first item in the 10 000-item list must succeed even though the
    /// list is virtualised (the first container is always materialised).
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_FirstItem_ByIndex_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found — TestWpfApp must define this fixture.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "Selecting index 0 in a virtualised ListBox must succeed.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.NewValue, Is.EqualTo("Item 0"),
            "newValue must reflect the item at index 0.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — item near the end (off-screen, scroll-materialise path)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting an item far down the 10 000-item virtualised list requires
    /// the scroll-materialise loop to bring the container into view before
    /// the selection is applied.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_FarItem_ByIndex_ScrollMaterializes_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        // Index 9 999 is the last item in the 10k list — definitely off-screen.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "9999", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True,
            "Selecting the last item in a virtualised ListBox must succeed after scroll-materialisation.");
        Assert.That(result.NewValue, Is.EqualTo("Item 9999"),
            "newValue must reflect the last item.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — mid-list item (off-screen)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting a mid-list item (index 5000) exercises the scroll-materialise loop
    /// for an item that is off-screen but not at the end.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_MidItem_ByIndex_ScrollMaterializes_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "5000", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True,
            "Selecting a mid-list item in a virtualised ListBox must succeed.");
        Assert.That(result.NewValue, Is.EqualTo("Item 5000"),
            "newValue must reflect index 5000.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — exact text match (off-screen item)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting an off-screen item by exact text must trigger the scroll-materialise
    /// path and return success.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_ByExactText_FarItem_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Item 8000", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True,
            "Selecting by exact text in a virtualised ListBox must succeed.");
        Assert.That(result.NewValue, Is.EqualTo("Item 8000"),
            "newValue must be the matched item text.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — partial text (unambiguous)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A substring that matches exactly one item in the 10k list must succeed.
    /// "Item 9999" is the only item matching "9999".
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_ByPartialText_Unambiguous_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        // "9999" appears only in "Item 9999".
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "9999", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True,
            "Unambiguous partial text must succeed in a virtualised ListBox.");
        Assert.That(result.NewValue, Is.EqualTo("Item 9999"),
            "newValue must be the uniquely matched item.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — partial text (ambiguous) → LocatorAmbiguous
    // -------------------------------------------------------------------------

    /// <summary>
    /// A partial text that matches many items must return LocatorAmbiguous so that
    /// callers are never silently wrong.
    /// "Item" matches all 10 000 items in the list.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_ByPartialText_Ambiguous_ReturnsLocatorAmbiguous()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        // "Item" is a substring of every entry ("Item 0" … "Item 9999") and is not
        // an exact match for any, so the partial path must return LocatorAmbiguous.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Item", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Ambiguous partial text must fail.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.LocatorAmbiguous),
            "failureReason must be LocatorAmbiguous when multiple items match.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — out-of-range index → ElementNotFound
    // -------------------------------------------------------------------------

    /// <summary>
    /// An out-of-range index must return ElementNotFound even for a virtualised list.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_OutOfRangeIndex_ReturnsElementNotFound()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "10000", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Out-of-range index must fail.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.ElementNotFound),
            "failureReason must be ElementNotFound for an out-of-range index.");
    }

    // -------------------------------------------------------------------------
    // Virtualized list — re-select same item → stateChanged=false
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting the already-selected item in a virtualised list must return
    /// stateChanged=false.
    /// </summary>
    [Test]
    public async Task SelectItem_BigList_SameItem_StateChangedFalse()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testBigList").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testBigList not found.");
            return;
        }

        // First selection — establish a baseline at index 0.
        await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        // Re-select the same item.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when the selection does not change.");
    }
}
