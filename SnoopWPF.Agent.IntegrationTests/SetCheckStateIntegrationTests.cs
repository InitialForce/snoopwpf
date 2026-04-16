namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_set_check_state</c> (M2-03).
/// Covers the acceptance scenarios from the bead:
/// <list type="number">
///   <item>CheckBox round-trip: set checked → reads back correctly.</item>
///   <item>CheckBox indeterminate: set indeterminate → stateChanged=true.</item>
///   <item>RadioButton: set checked → success.</item>
///   <item>Bare ToggleButton: rejected with PatternNotSupported + wpf_toggle suggestion.</item>
///   <item>Mutation disabled → MutationDisabled thrown from inspector.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class SetCheckStateIntegrationTests : WpfIntegrationTestBase
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
    // Scenario 1: CheckBox round-trip
    // -------------------------------------------------------------------------

    /// <summary>
    /// Setting a CheckBox to "checked" must succeed and return newValue="checked".
    /// </summary>
    [Test]
    public async Task SetCheckState_CheckBox_Checked_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found — TestWpfApp must be updated.");
            return;
        }

        // Reset to unchecked first.
        await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "unchecked", ct: default)
            .ConfigureAwait(false);

        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SetCheckStateAsync must succeed on a CheckBox.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.NewValue, Is.EqualTo("checked"),
            "newValue must be 'checked' after setting checked.");
        Assert.That(result.StateChanged, Is.True,
            "stateChanged must be true when state changes from unchecked to checked.");
    }

    /// <summary>
    /// Setting a CheckBox to "unchecked" must succeed and return newValue="unchecked".
    /// </summary>
    [Test]
    public async Task SetCheckState_CheckBox_Unchecked_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found.");
            return;
        }

        // Set to checked first.
        await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "unchecked", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NewValue, Is.EqualTo("unchecked"),
            "newValue must be 'unchecked' after setting unchecked.");
        Assert.That(result.StateChanged, Is.True);
    }

    /// <summary>
    /// Setting the same state twice: second call should report stateChanged=false.
    /// </summary>
    [Test]
    public async Task SetCheckState_CheckBox_SameState_StateChangedFalse()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found.");
            return;
        }

        // Establish baseline.
        await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        // Set the same state again.
        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when the state does not change.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: CheckBox indeterminate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Setting a three-state CheckBox to "indeterminate" must succeed and return
    /// newValue="indeterminate".
    /// </summary>
    [Test]
    public async Task SetCheckState_CheckBox_Indeterminate_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found.");
            return;
        }

        // Reset to checked first.
        await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "indeterminate", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True, "SetCheckStateAsync must succeed with indeterminate on a three-state CheckBox.");
        Assert.That(result.NewValue, Is.EqualTo("indeterminate"),
            "newValue must be 'indeterminate'.");
        Assert.That(result.StateChanged, Is.True,
            "stateChanged must be true when moving from checked to indeterminate.");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: RadioButton
    // -------------------------------------------------------------------------

    /// <summary>
    /// Setting a RadioButton to "checked" must succeed.
    /// </summary>
    [Test]
    public async Task SetCheckState_RadioButton_Checked_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testRadioButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testRadioButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SetCheckStateAsync must succeed on a RadioButton.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.NewValue, Is.EqualTo("checked"),
            "newValue must be 'checked' after setting checked on a RadioButton.");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Bare ToggleButton → PatternNotSupported + wpf_toggle suggestion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calling SetCheckStateAsync on a bare ToggleButton must fail with PatternNotSupported
    /// and a suggestion to use wpf_toggle.
    /// </summary>
    [Test]
    public async Task SetCheckState_ToggleButton_ReturnsPatternNotSupported_WithWpfToggleSuggestion()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testToggleButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testToggleButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .SetCheckStateAsync(nodeId, "checked", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false for bare ToggleButton.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported for bare ToggleButton.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "A suggestion must be provided for ToggleButton rejection.");
        Assert.That(result.Suggestion!.Tool, Is.EqualTo("wpf_toggle"),
            "The suggestion tool must be 'wpf_toggle'.");
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Mutation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// SetCheckStateAsync with EnableMutation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task SetCheckState_MutationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found.");
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
                .SetCheckStateAsync(nodeId, "checked", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when mutation is disabled.");
    }
}
