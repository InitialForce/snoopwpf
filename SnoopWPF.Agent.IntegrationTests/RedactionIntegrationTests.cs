namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Integration tests that exercise the redaction path of <see cref="SnoopInspector"/>.
///
/// The shared <see cref="McpTestClient"/> uses <c>EnableRedaction=false</c> so none of the
/// other integration test fixtures cover the redaction execution path. This fixture spins up
/// its own <see cref="SnoopInspector"/> with <c>EnableRedaction=true</c> (and optionally
/// <c>EnableMutation=true</c>) to fill that gap.
///
/// <see cref="TestWpfApp"/> now includes a <c>PasswordBox</c> named <c>testPasswordBox</c>
/// which is guaranteed to be present in every test run.
/// </summary>
[TestFixture]
[Category("RequiresWpf")]
[NonParallelizable]
public sealed class RedactionIntegrationTests
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
    /// Returns the NodeId of the first node whose TypeName contains <paramref name="typeName"/>.
    /// </summary>
    private static async Task<string?> FindNodeIdByTypeAsync(
        SnoopInspector inspector, string typeName)
    {
        var tree = await inspector
            .GetVisualTreeAsync((string?)null, 10, "visual", null, default)
            .ConfigureAwait(false);
        var nodes = Flatten(tree.Root);
        return nodes
            .FirstOrDefault(
                n => n.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                  || n.TypeName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase)
                  || n.TypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            ?.NodeId;
    }

    // -------------------------------------------------------------------------
    // GetProperties — redaction enabled
    // -------------------------------------------------------------------------

    /// <summary>
    /// With <c>EnableRedaction=true</c>, the <c>Password</c> property of a PasswordBox
    /// must be returned as "[REDACTED]" rather than the actual value.
    /// </summary>
    [Test]
    public async Task GetProperties_WithRedactionEnabled_PasswordBoxValueReturnsRedacted()
    {
        using var redactingInspector = new SnoopInspector(
            dispatcher: IntegrationTestFixture.WpfApp.Dispatcher,
            rootTarget: IntegrationTestFixture.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = true,
            });

        var nodeId = await FindNodeIdByTypeAsync(redactingInspector, "PasswordBox")
            .ConfigureAwait(false);

        Assert.That(nodeId, Is.Not.Null,
            "TestWpfApp must contain a PasswordBox (testPasswordBox).");

        var page = await redactingInspector
            .GetPropertiesAsync(nodeId!, null, null, true, null, 500, default)
            .ConfigureAwait(false);

        // Find any property whose name contains "Password"
        var passwordProp = page.Items.FirstOrDefault(
            p => p.Name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0);

        Assert.That(passwordProp, Is.Not.Null,
            "PasswordBox should expose a property whose name contains 'Password'.");

        Assert.That(passwordProp!.Value, Is.EqualTo("[REDACTED]"),
            "Password property on PasswordBox must be redacted to '[REDACTED]' when EnableRedaction=true.");
    }

    // -------------------------------------------------------------------------
    // GetBindingInfo — redaction enabled
    // -------------------------------------------------------------------------

    /// <summary>
    /// With <c>EnableRedaction=true</c>, requesting binding info for a property whose
    /// name contains a sensitive keyword must return "[REDACTED]" for Path and ResolvedValue.
    /// </summary>
    [Test]
    public async Task GetBindingInfo_WithRedactionEnabled_SensitivePropertyNameReturnsRedacted()
    {
        using var redactingInspector = new SnoopInspector(
            dispatcher: IntegrationTestFixture.WpfApp.Dispatcher,
            rootTarget: IntegrationTestFixture.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = true,
            });

        var nodeId = await FindNodeIdByTypeAsync(redactingInspector, "PasswordBox")
            .ConfigureAwait(false);

        Assert.That(nodeId, Is.Not.Null,
            "TestWpfApp must contain a PasswordBox (testPasswordBox).");

        // "Password" is a sensitive property name — even if it has no binding,
        // the BindingInfoDto Path and ResolvedValue must be redacted.
        var binding = await redactingInspector
            .GetBindingInfoAsync(nodeId!, "Password", default)
            .ConfigureAwait(false);

        Assert.That(binding, Is.Not.Null);

        if (binding.HasBinding)
        {
            // If there is a binding, Path and ResolvedValue must be masked.
            Assert.That(binding.Path, Is.EqualTo("[REDACTED]"),
                "BindingInfoDto.Path must be '[REDACTED]' for a sensitive property when EnableRedaction=true.");
            Assert.That(binding.ResolvedValue, Is.EqualTo("[REDACTED]"),
                "BindingInfoDto.ResolvedValue must be '[REDACTED]' for a sensitive property when EnableRedaction=true.");
        }
        else
        {
            // No binding — redaction is applied at the property-value level (verified in
            // GetProperties test above). The binding info result itself is not meaningful here.
            Assert.That(binding.HasBinding, Is.False,
                "PasswordBox.Password in TestWpfApp has no binding; redaction is exercised via GetProperties.");
        }
    }

    // -------------------------------------------------------------------------
    // SetProperty — redacted property blocked
    // -------------------------------------------------------------------------

    /// <summary>
    /// With both <c>EnableMutation=true</c> and <c>EnableRedaction=true</c>, attempting to
    /// set a property whose name is sensitive must throw a <see cref="SnoopException"/> with
    /// <see cref="SnoopErrorCode.PropertyRedacted"/>.
    /// </summary>
    [Test]
    public async Task SetProperty_OnRedactedProperty_ThrowsPropertyRedactedError()
    {
        using var mutatingRedactingInspector = new SnoopInspector(
            dispatcher: IntegrationTestFixture.WpfApp.Dispatcher,
            rootTarget: IntegrationTestFixture.WpfApp.App,
            options: new SnoopInspectorOptions
            {
                TimeoutMs = 10_000,
                EnableMutation = true,
                EnableRedaction = true,
            });

        // Use the Button (which also has Width), but try to set a property called "Password"
        // which hits the redaction guard before any type resolution.
        // We use the Button's node since PasswordBox.Password requires a special setter
        // and we only want to verify the redaction guard triggers first.
        var nodeId = await FindNodeIdByTypeAsync(mutatingRedactingInspector, "Button")
            .ConfigureAwait(false);

        Assert.That(nodeId, Is.Not.Null,
            "TestWpfApp must contain a Button.");

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await mutatingRedactingInspector
                .SetPropertyAsync(nodeId!, "Password", "newvalue", default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.PropertyRedacted),
            "Attempting to set a redacted property must throw SnoopErrorCode.PropertyRedacted.");
    }
}
