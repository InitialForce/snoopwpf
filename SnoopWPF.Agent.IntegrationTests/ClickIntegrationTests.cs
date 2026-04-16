namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_click</c> (M2-05).
/// Covers the acceptance criteria from the bead:
/// <list type="number">
///   <item>UIElement with IInvokeProvider → success.</item>
///   <item>Element with Command bound → success + wpf_execute_command hint in suggestion.</item>
///   <item>Element without IInvokeProvider → PatternNotSupported.</item>
///   <item>Automation disabled → exception thrown.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class ClickIntegrationTests : WpfIntegrationTestBase
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
    // Scenario 1: UIElement with IInvokeProvider → success
    // -------------------------------------------------------------------------

    /// <summary>
    /// A Button with InvokePattern support and no Command should succeed.
    /// ChosenTier must be L1.
    /// </summary>
    [Test]
    public async Task Click_InvokableElement_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testClickableButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testClickableButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ClickAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "ClickAsync must return a non-null result.");
        Assert.That(result.Success, Is.True, "Click must succeed on an invokable element.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.ChosenTier, Is.EqualTo(InputTier.L1), "ChosenTier must be L1.");
    }

    /// <summary>
    /// A Button with no Command should not carry a wpf_execute_command suggestion.
    /// </summary>
    [Test]
    public async Task Click_InvokableElementNoCommand_NoExecuteCommandSuggestion()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testClickableButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testClickableButton not found.");
            return;
        }

        var result = await this.Client.Inspector
            .ClickAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        // No Command bound → suggestion must be null.
        Assert.That(result.Suggestion, Is.Null,
            "No wpf_execute_command suggestion expected when no Command is bound.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Element with Command bound → success + hint
    // -------------------------------------------------------------------------

    /// <summary>
    /// A Button with a Command bound should succeed AND carry a
    /// wpf_execute_command suggestion hinting the caller to prefer L0.
    /// </summary>
    [Test]
    public async Task Click_ElementWithCommand_ReturnsSuccessAndHint()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testClickableWithCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testClickableWithCommandButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ClickAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True,
            "Click must succeed even when a Command is bound.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "A wpf_execute_command suggestion must be present when a Command is bound.");
        Assert.That(result.Suggestion!.Tool, Is.EqualTo("wpf_execute_command"),
            "Suggestion tool must be wpf_execute_command.");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Element without IInvokeProvider → PatternNotSupported
    // -------------------------------------------------------------------------

    /// <summary>
    /// A TextBlock (no IInvokeProvider) must fail with PatternNotSupported.
    /// </summary>
    [Test]
    public async Task Click_NonInvokableElement_ReturnsPatternNotSupported()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testNonInvokableElement").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testNonInvokableElement not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ClickAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False,
            "Click must fail on an element that does not support IInvokeProvider.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported.");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Automation disabled → AutomationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// ClickAsync with EnableAutomation=false must throw SnoopException.
    /// </summary>
    [Test]
    public async Task Click_AutomationDisabled_ThrowsSnoopException()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testClickableButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testClickableButton not found.");
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
                .ClickAsync(nodeId, ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled (the automation-disabled gate) " +
            "when EnableAutomation=false.");
    }
}
