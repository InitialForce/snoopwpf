namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_execute_command</c> (M2-01).
/// Covers the four acceptance scenarios from the bead:
/// <list type="number">
///   <item>Button with Command + CanExecute=true → success + stateChanged false (no window opened).</item>
///   <item>Button with Command + CanExecute=false → CannotExecuteCommand + suggestion.</item>
///   <item>Button without Command → PatternNotSupported.</item>
///   <item>Mutation disabled → MutationDisabled thrown from inspector.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class ExecuteCommandIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the node ID of the first element whose Name equals <paramref name="elementName"/>.
    /// </summary>
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
    // Scenario 1: Button with Command + CanExecute = true → success
    // -------------------------------------------------------------------------

    /// <summary>
    /// A button with a relay command whose CanExecute=true should succeed.
    /// StateChanged is false when the command does not open a new window.
    /// </summary>
    [Test]
    public async Task ExecuteCommand_CanExecuteTrue_ReturnsSuccess()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExecutableCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExecutableCommandButton not found — TestWpfApp must be updated.");
            return;
        }

        var result = await this.Client.Inspector
            .ExecuteCommandAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null, "ExecuteCommandAsync must return a non-null result.");
        Assert.That(result.Success, Is.True, "Execution must succeed when CanExecute=true.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.Suggestion, Is.Null, "suggestion must be null on success.");
    }

    /// <summary>
    /// TreeVersionDelta must be zero when the command does not open or close windows.
    /// </summary>
    [Test]
    public async Task ExecuteCommand_CanExecuteTrue_StateChangedReflectsWindowDelta()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExecutableCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExecutableCommandButton not found.");
            return;
        }

        var result = await this.Client.Inspector
            .ExecuteCommandAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        // The relay command does not open windows, so treeVersionDelta must be 0.
        Assert.That(result.TreeVersionDelta, Is.EqualTo(0),
            "treeVersionDelta must be 0 when no window is opened.");
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when no window was opened.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Button with Command + CanExecute = false → CannotExecuteCommand
    // -------------------------------------------------------------------------

    /// <summary>
    /// A button whose relay command returns CanExecute=false must fail with
    /// CannotExecuteCommand and a non-null suggestion.
    /// </summary>
    [Test]
    public async Task ExecuteCommand_CanExecuteFalse_ReturnsCannotExecuteCommand()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testNonExecutableCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testNonExecutableCommandButton not found.");
            return;
        }

        var result = await this.Client.Inspector
            .ExecuteCommandAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false when CanExecute=false.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.CannotExecuteCommand),
            "failureReason must be CannotExecuteCommand.");
        Assert.That(result.Suggestion, Is.Not.Null,
            "A suggestion must be provided for CannotExecuteCommand.");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Button without Command → PatternNotSupported
    // -------------------------------------------------------------------------

    /// <summary>
    /// A button with no Command bound must fail with PatternNotSupported.
    /// </summary>
    [Test]
    public async Task ExecuteCommand_NoCommand_ReturnsPatternNotSupported()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testNoCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testNoCommandButton not found.");
            return;
        }

        var result = await this.Client.Inspector
            .ExecuteCommandAsync(nodeId, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False, "Success must be false when no command is bound.");
        Assert.That(result.FailureReason, Is.EqualTo(FailureReason.PatternNotSupported),
            "failureReason must be PatternNotSupported.");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Mutation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// ExecuteCommandAsync with EnableMutation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task ExecuteCommand_MutationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testExecutableCommandButton").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testExecutableCommandButton not found.");
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
                .ExecuteCommandAsync(nodeId, ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when mutation is disabled.");
    }
}
