namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests for <c>wpf_set_text_value</c> (M2-02).
/// Covers the acceptance scenarios from the bead:
/// <list type="number">
///   <item>TextBox round-trip: set value → reads back correctly.</item>
///   <item>PasswordBox round-trip: set value → success; previousValue always redacted.</item>
///   <item>PasswordBox sensitive retention gate: AllowSensitiveRetention=false → newValue redacted.</item>
///   <item>Mutation disabled → MutationDisabled thrown from inspector.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class SetTextValueIntegrationTests : WpfIntegrationTestBase
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
    // Scenario 1: TextBox round-trip
    // -------------------------------------------------------------------------

    /// <summary>
    /// Setting text on a TextBox must succeed, return the correct newValue,
    /// and the TextBox.Text must reflect the new value.
    /// </summary>
    [Test]
    public async Task SetTextValue_TextBox_Success_ReturnsCorrectNewValue()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testTextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testTextBox not found — TestWpfApp must be updated.");
            return;
        }

        const string newText = "Hello from SetTextValue";
        var result = await this.Client.Inspector
            .SetTextValueAsync(nodeId, newText, ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SetTextValueAsync must succeed on a TextBox.");
        Assert.That(result.FailureReason, Is.Null, "failureReason must be null on success.");
        Assert.That(result.NewValue, Is.EqualTo(newText),
            "newValue must equal the value that was set.");
        Assert.That(result.StateChanged, Is.True,
            "stateChanged must be true when text changes.");
    }

    /// <summary>
    /// Setting the same text twice: second call should report stateChanged=false.
    /// </summary>
    [Test]
    public async Task SetTextValue_TextBox_SameValue_StateChangedFalse()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testTextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testTextBox not found.");
            return;
        }

        const string sameText = "Idempotent";

        // Set once to establish baseline.
        await this.Client.Inspector
            .SetTextValueAsync(nodeId, sameText, ct: default)
            .ConfigureAwait(false);

        // Set again with the same value.
        var result = await this.Client.Inspector
            .SetTextValueAsync(nodeId, sameText, ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StateChanged, Is.False,
            "stateChanged must be false when the value does not change.");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: PasswordBox round-trip — previousValue is always redacted
    // -------------------------------------------------------------------------

    /// <summary>
    /// Setting password on a PasswordBox must succeed.
    /// previousValue must always be '[REDACTED]' (S3 policy).
    /// </summary>
    [Test]
    public async Task SetTextValue_PasswordBox_Success_PreviousValueRedacted()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testPasswordBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testPasswordBox not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SetTextValueAsync(nodeId, "newPass123", ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, "SetTextValueAsync must succeed on a PasswordBox.");
        Assert.That(result.PreviousValue, Is.EqualTo("[REDACTED]"),
            "previousValue must be '[REDACTED]' for PasswordBox (S3).");
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Sensitive retention gate — newValue redacted unless allowed
    // -------------------------------------------------------------------------

    /// <summary>
    /// When AllowSensitiveRetention=false (default), newValue must be '[REDACTED]'
    /// for PasswordBox responses.
    /// </summary>
    [Test]
    public async Task SetTextValue_PasswordBox_SensitiveRetentionGate_NewValueRedacted()
    {
        // The shared McpTestClient has AllowSensitiveRetention=false (default).
        var nodeId = await this.FindNodeIdByNameAsync("testPasswordBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testPasswordBox not found.");
            return;
        }

        var result = await this.Client.Inspector
            .SetTextValueAsync(nodeId, "s3cr3t", ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NewValue, Is.EqualTo("[REDACTED]"),
            "newValue must be '[REDACTED]' when AllowSensitiveRetention=false (default S3 gate).");
    }

    // -------------------------------------------------------------------------
    // Scenario 4: Mutation disabled → MutationDisabled exception
    // -------------------------------------------------------------------------

    /// <summary>
    /// SetTextValueAsync with EnableMutation=false must throw SnoopException
    /// with code MutationDisabled.
    /// </summary>
    [Test]
    public async Task SetTextValue_MutationDisabled_ThrowsMutationDisabled()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testTextBox").ConfigureAwait(false);

        if (nodeId == null)
        {
            Assert.Fail("testTextBox not found.");
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
                .SetTextValueAsync(nodeId, "should fail", ct: default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.MutationDisabled),
            "SnoopException code must be MutationDisabled when mutation is disabled.");
    }
}
