namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_select_item</c> (M2-04a, L0 basic — non-virtualized).
/// Covers the acceptance scenarios from the bead:
/// <list type="number">
///   <item>ListBox: select by index → success, correct newValue.</item>
///   <item>ListBox: select by exact text → success.</item>
///   <item>ListBox: select by partial text (unambiguous) → success.</item>
///   <item>ListBox: ambiguous partial text → LocatorAmbiguous.</item>
///   <item>ListBox: out-of-range index → ElementNotFound.</item>
///   <item>ListBox: selecting the same item twice → stateChanged=false.</item>
///   <item>ComboBox: select by index → success.</item>
///   <item>ComboBox: select by exact text → success.</item>
///   <item>Mutation disabled → MutationDisabled thrown.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class SelectItemBasicIntegrationTests : WpfIntegrationTestBase
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
    // ListBox — index-based selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting an item by zero-based index in a ListBox must succeed and return
    /// the correct newValue.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_ByIndex_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "1", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SelectItemAsync must succeed when selecting by index.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.NewValue, Is.EqualTo("Item Two"),
            "newValue must reflect the item at index 1.");
        Assert.That(result.StateChanged, Is.True,
            "stateChanged must be true when the selection changes.");
    }

    // -------------------------------------------------------------------------
    // ListBox — exact text selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting an item by exact text (case-insensitive) must succeed.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_ByExactText_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        // First deselect.
        await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Item Three", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NewValue, Is.EqualTo("Item Three"),
            "Exact text match must select the correct item.");
    }

    // -------------------------------------------------------------------------
    // ListBox — partial text (unambiguous substring)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting an item via an unambiguous substring must succeed.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_ByPartialText_Unambiguous_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        // "Two" only matches "Item Two".
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Two", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True,
            "Unambiguous partial text match must succeed.");
        Assert.That(result.NewValue, Is.EqualTo("Item Two"),
            "newValue must reflect the matched item.");
    }

    // -------------------------------------------------------------------------
    // ListBox — ambiguous partial text → LocatorAmbiguous
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the partial text matches more than one item, LocatorAmbiguous must be returned.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_ByPartialText_Ambiguous_ReturnsLocatorAmbiguous()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        // "Item" matches all three items → ambiguous.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Item", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Ambiguous partial text must fail.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.LocatorAmbiguous),
            "failureReason must be LocatorAmbiguous when multiple items match.");
    }

    // -------------------------------------------------------------------------
    // ListBox — out-of-range index → ElementNotFound
    // -------------------------------------------------------------------------

    /// <summary>
    /// Using an out-of-range index must return ElementNotFound.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_OutOfRangeIndex_ReturnsElementNotFound()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "999", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Out-of-range index must fail.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.ElementNotFound),
            "failureReason must be ElementNotFound for an out-of-range index.");
    }

    // -------------------------------------------------------------------------
    // ListBox — selecting the same item → stateChanged=false
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting the already-selected item must return stateChanged=false.
    /// </summary>
    [Test]
    public async Task SelectItem_ListBox_SameItem_StateChangedFalse()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        // Establish baseline.
        await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        // Select the same item again.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "0", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when the selection does not change.");
    }

    // -------------------------------------------------------------------------
    // ComboBox — index selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting a ComboBox item by index must succeed and return the correct newValue.
    /// </summary>
    [Test]
    public async Task SelectItem_ComboBox_ByIndex_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testComboBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testComboBox not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "2", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SelectItemAsync must succeed on a ComboBox.");
        Assert.That(result.NewValue, Is.EqualTo("Cherry"),
            "newValue must reflect the item at index 2.");
    }

    // -------------------------------------------------------------------------
    // ComboBox — exact text selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selecting a ComboBox item by exact text must succeed.
    /// </summary>
    [Test]
    public async Task SelectItem_ComboBox_ByExactText_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testComboBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testComboBox not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId, "Banana", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NewValue, Is.EqualTo("Banana"),
            "Exact text match must select the correct item.");
    }

    // -------------------------------------------------------------------------
    // Mutation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// SelectItemAsync with EnableMutation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task SelectItem_MutationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testListBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testListBox not found.");
            return;
        }

        using var readOnlyInspector = new SnoopInspector(
            dispatcher: this.WpfApp.Dispatcher,
            rootTarget: this.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await readOnlyInspector
                .SelectItemAsync(nodeId, "0", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when mutation is disabled.");
    }
}
