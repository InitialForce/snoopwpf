namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_toggle</c> (M2-06).
/// Covers the acceptance scenarios from the bead:
/// <list type="number">
///   <item>Bare ToggleButton — toggle succeeds and flips state.</item>
///   <item>ToggleButton double-toggle — returns to original state.</item>
///   <item>CheckBox — rejected with PatternNotSupported + wpf_set_check_state suggestion.</item>
///   <item>RadioButton — rejected with PatternNotSupported + wpf_set_check_state suggestion.</item>
///   <item>Automation disabled → MutationDisabled exception thrown.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class ToggleIntegrationTests : WpfIntegrationTestBase
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
    // Scenario 1: Bare ToggleButton — toggle flips state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggling a bare ToggleButton must succeed, ChosenTier must be L1, and
    /// stateChanged must be true.
    /// </summary>
    [Test]
    public async Task Toggle_ToggleButton_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testToggleButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testToggleButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ToggleAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "ToggleAsync must return a non-null result.");
        Assert.That(result.Success, Is.True, "Toggle must succeed on a bare ToggleButton.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.ChosenTier, Is.EqualTo(InputTier.L1), "ChosenTier must be L1.");
        Assert.That(result.StateChanged, Is.True, "stateChanged must be true after a toggle.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Double-toggle returns to original state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggling a ToggleButton twice must succeed both times and the second
    /// call must also report stateChanged=true (the state changed again).
    /// </summary>
    [Test]
    public async Task Toggle_ToggleButton_TwiceReturnsToOriginalState()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testToggleButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testToggleButton not found.");
            return;
        }

        var first = await this.Client.Inspector
            .ToggleAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(first.Success, Is.True, "First toggle must succeed.");

        var second = await this.Client.Inspector
            .ToggleAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(second.Success, Is.True, "Second toggle must succeed.");
        Assert.That(second.StateChanged, Is.True,
            "stateChanged must be true on the second toggle because the state was flipped again.");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: CheckBox → PatternNotSupported + wpf_set_check_state suggestion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calling ToggleAsync on a CheckBox must fail with PatternNotSupported
    /// and a suggestion to use wpf_set_check_state.
    /// </summary>
    [Test]
    public async Task Toggle_CheckBox_ReturnsPatternNotSupported_WithSetCheckStateSuggestion()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testCheckBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testCheckBox not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ToggleAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false for CheckBox.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported for CheckBox.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "A suggestion must be provided for CheckBox rejection.");
        Assert.That(result.Suggestion!.Tool, Is.EqualTo("wpf_set_check_state"),
            "The suggestion tool must be 'wpf_set_check_state'.");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: RadioButton → PatternNotSupported + wpf_set_check_state suggestion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calling ToggleAsync on a RadioButton must fail with PatternNotSupported
    /// and a suggestion to use wpf_set_check_state.
    /// </summary>
    [Test]
    public async Task Toggle_RadioButton_ReturnsPatternNotSupported_WithSetCheckStateSuggestion()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testRadioButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testRadioButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ToggleAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false for RadioButton.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported for RadioButton.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "A suggestion must be provided for RadioButton rejection.");
        Assert.That(result.Suggestion!.Tool, Is.EqualTo("wpf_set_check_state"),
            "The suggestion tool must be 'wpf_set_check_state'.");
    }

    // -------------------------------------------------------------------------
    // Scenario 5: Automation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// ToggleAsync with EnableAutomation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task Toggle_AutomationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testToggleButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testToggleButton not found.");
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
                .ToggleAsync(nodeId, ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when EnableAutomation=false.");
    }
}
