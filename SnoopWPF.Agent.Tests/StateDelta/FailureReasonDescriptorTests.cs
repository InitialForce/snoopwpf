namespace SnoopWPF.Agent.Tests.StateDelta;

using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.StateDelta;

/// <summary>
/// Tests every <see cref="FailureReason"/> enum value maps to the PRD §7.4 table entry
/// as produced by <see cref="FailureReasonDescriptor.Suggest"/>.
/// </summary>
[TestFixture]
public class FailureReasonDescriptorTests
{
    private static readonly WpfLocator SampleLocator = new()
    {
        Form = WpfLocatorForm.AutomationId,
        Value = "btnOk",
        Raw = "#btnOk",
    };

    // ── Null-returning values (session reconfig required) ────────────────────────

    [Test]
    public void AutomationDisabled_ReturnsNull()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.AutomationDisabled, SampleLocator);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void MutationDisabled_ReturnsNull()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.MutationDisabled, SampleLocator);
        Assert.That(result, Is.Null);
    }

    // ── ElementNotFound ──────────────────────────────────────────────────────────

    [Test]
    public void ElementNotFound_ReturnsFindElements()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.ElementNotFound, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_find_elements"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "query" && a.Value == SampleLocator.Raw));
    }

    [Test]
    public void ElementNotFound_WithNullContext_EmptyQueryArg()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.ElementNotFound, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_find_elements"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "query" && string.IsNullOrEmpty(a.Value)));
    }

    // ── LocatorAmbiguous ─────────────────────────────────────────────────────────

    [Test]
    public void LocatorAmbiguous_ReturnsFindElements_WithHint()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.LocatorAmbiguous, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_find_elements"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── ElementNotVisible ────────────────────────────────────────────────────────

    [Test]
    public void ElementNotVisible_ReturnsWaitForProperty_IsVisible()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.ElementNotVisible, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_wait_for_property"));
        AssertArg(result, "propertyName",  "IsVisible");
        AssertArg(result, "expectedValue", "true");
        AssertArg(result, "timeoutMs",     "5000");
    }

    // ── ElementNotEnabled ────────────────────────────────────────────────────────

    [Test]
    public void ElementNotEnabled_ReturnsWaitForProperty_IsEnabled()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.ElementNotEnabled, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_wait_for_property"));
        AssertArg(result, "propertyName",  "IsEnabled");
        AssertArg(result, "expectedValue", "true");
        AssertArg(result, "timeoutMs",     "5000");
    }

    // ── ElementOutsideViewport ───────────────────────────────────────────────────

    [Test]
    public void ElementOutsideViewport_ReturnsSelectItem()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.ElementOutsideViewport, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_select_item"));
        AssertArg(result, "locator", SampleLocator.Raw);
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── CannotExecuteCommand ─────────────────────────────────────────────────────

    [Test]
    public void CannotExecuteCommand_ReturnsInspectElement()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.CannotExecuteCommand, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_inspect_element"));
        AssertArg(result, "propertyName", "Command.CanExecute");
    }

    // ── PatternNotSupported ──────────────────────────────────────────────────────

    [Test]
    public void PatternNotSupported_ReturnsInspectElement_WithHint()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_inspect_element"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── TierMismatch ─────────────────────────────────────────────────────────────

    [Test]
    public void TierMismatch_ReturnsExecuteCommand_WithFallbackHint()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.TierMismatch, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_execute_command"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── StateUnchanged ───────────────────────────────────────────────────────────

    [Test]
    public void StateUnchanged_ReturnsWaitForProperty_WithHint()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.StateUnchanged, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_wait_for_property"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── DispatcherBusy ───────────────────────────────────────────────────────────

    [Test]
    public void DispatcherBusy_ReturnsWaitForProperty_WithHint()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.DispatcherBusy, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("wpf_wait_for_property"));
        Assert.That(result.Args, Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == "hint"));
    }

    // ── TargetNotRunning ─────────────────────────────────────────────────────────

    [Test]
    public void TargetNotRunning_ReturnsBrokerLaunchTarget()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.TargetNotRunning, SampleLocator);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("broker_launch_target"));
    }

    [Test]
    public void TargetNotRunning_WithNullContext_StillReturnsSuggestion()
    {
        var result = FailureReasonDescriptor.Suggest(FailureReason.TargetNotRunning, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tool, Is.EqualTo("broker_launch_target"));
    }

    // ── All 13 values are covered (completeness guard) ───────────────────────────

    [Test]
    public void AllEnumValues_ReturnSuggestionOrNull_NoException(
        [Values(
            FailureReason.ElementNotFound,
            FailureReason.ElementNotVisible,
            FailureReason.ElementNotEnabled,
            FailureReason.CannotExecuteCommand,
            FailureReason.AutomationDisabled,
            FailureReason.MutationDisabled,
            FailureReason.TierMismatch,
            FailureReason.StateUnchanged,
            FailureReason.LocatorAmbiguous,
            FailureReason.DispatcherBusy,
            FailureReason.ElementOutsideViewport,
            FailureReason.PatternNotSupported,
            FailureReason.TargetNotRunning)]
        FailureReason reason)
    {
        Assert.DoesNotThrow(() => FailureReasonDescriptor.Suggest(reason, SampleLocator));
    }

    [Test]
    public void NonNullSuggestions_HaveNonEmptyTool()
    {
        foreach (FailureReason reason in System.Enum.GetValues(typeof(FailureReason)))
        {
            var result = FailureReasonDescriptor.Suggest(reason, SampleLocator);
            if (result is not null)
            {
                Assert.That(result.Tool, Is.Not.Empty, $"Tool must not be empty for {reason}");
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static void AssertArg(Contracts.Dtos.SuggestionDto dto, string name, string expectedValue)
    {
        Assert.That(
            dto.Args,
            Has.Some.Matches<Contracts.Dtos.NameValuePairDto>(a => a.Name == name && a.Value == expectedValue),
            $"Expected arg '{name}' = '{expectedValue}'");
    }
}
