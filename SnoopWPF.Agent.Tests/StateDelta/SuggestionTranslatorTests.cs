namespace SnoopWPF.Agent.Tests.StateDelta;

using System;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.StateDelta;

/// <summary>
/// Contract tests for <see cref="ISuggestionTranslator"/> / <see cref="DefaultSuggestionTranslator"/>.
/// Verifies that every <see cref="FailureReason"/> enum value is handled without throwing,
/// that known actionable reasons return a non-null <see cref="Contracts.Dtos.SuggestionDto"/>,
/// and that non-null results always carry a non-empty tool name.
/// </summary>
[TestFixture]
public class SuggestionTranslatorTests
{
    private ISuggestionTranslator translator = null!;

    private static readonly FailureContext SampleCtx = new()
    {
        Locator = new WpfLocator
        {
            Form = WpfLocatorForm.AutomationId,
            Value = "btnOk",
            Raw = "#btnOk",
        },
    };

    [SetUp]
    public void SetUp()
    {
        this.translator = new DefaultSuggestionTranslator();
    }

    // ── Values that MUST return a non-null SuggestionDto ──────────────────────────

    [TestCase(FailureReason.ElementNotFound)]
    [TestCase(FailureReason.ElementNotVisible)]
    [TestCase(FailureReason.ElementNotEnabled)]
    [TestCase(FailureReason.LocatorAmbiguous)]
    [TestCase(FailureReason.ElementOutsideViewport)]
    [TestCase(FailureReason.CannotExecuteCommand)]
    [TestCase(FailureReason.PatternNotSupported)]
    [TestCase(FailureReason.TierMismatch)]
    [TestCase(FailureReason.StateUnchanged)]
    [TestCase(FailureReason.DispatcherBusy)]
    [TestCase(FailureReason.TargetNotRunning)]
    [TestCase(FailureReason.ElementDisabled)] // FX6-C1: now has a descriptor entry
    public void NonNullReasons_ReturnNonNullSuggestion(FailureReason reason)
    {
        var result = this.translator.Translate(reason, SampleCtx);

        Assert.That(result, Is.Not.Null,
            $"{reason} should return a non-null SuggestionDto");
        Assert.That(result!.Tool, Is.Not.Empty,
            $"{reason} SuggestionDto.Tool must not be empty");
    }

    // ── Values that return null (no automated remediation) ────────────────────────
    // AutomationDisabled / MutationDisabled require operator-level config changes.
    // BlobNotFound has no descriptor entry yet (falls through to default null arm).

    [TestCase(FailureReason.AutomationDisabled)]
    [TestCase(FailureReason.MutationDisabled)]
    [TestCase(FailureReason.BlobNotFound)]
    public void NullReasons_ReturnNull(FailureReason reason)
    {
        var result = this.translator.Translate(reason, SampleCtx);

        Assert.That(result, Is.Null,
            $"{reason} should return null (no automated remediation)");
    }

    // ── Empty context (null locator) — no exceptions ──────────────────────────────

    [Test]
    public void AllReasons_WithEmptyContext_DoNotThrow()
    {
        foreach (FailureReason reason in Enum.GetValues(typeof(FailureReason)))
        {
            Assert.DoesNotThrow(
                () => this.translator.Translate(reason, FailureContext.Empty),
                $"Translate must not throw for {reason} with FailureContext.Empty");
        }
    }

    // ── All non-null suggestions carry non-empty tool names ───────────────────────

    [Test]
    public void AllNonNullSuggestions_HaveNonEmptyTool()
    {
        foreach (FailureReason reason in Enum.GetValues(typeof(FailureReason)))
        {
            var result = this.translator.Translate(reason, SampleCtx);
            if (result is not null)
            {
                Assert.That(result.Tool, Is.Not.Empty,
                    $"SuggestionDto.Tool must not be empty for {reason}");
            }
        }
    }

    // ── TargetNotRunning must produce BrokerLifecycle category ────────────────────

    [Test]
    public void TargetNotRunning_ReturnsBrokerLifecycleCategory()
    {
        var result = this.translator.Translate(FailureReason.TargetNotRunning, SampleCtx);

        Assert.That(result, Is.Not.Null);
        Assert.That(
            result!.Category,
            Is.EqualTo(SuggestionCategory.BrokerLifecycle),
            "TargetNotRunning must use BrokerLifecycle so consumer translators can rewrite the tool name.");
        Assert.That(
            result.Tool,
            Is.EqualTo("broker_launch_target"),
            "Advisory generic name must remain broker_launch_target for translator compatibility.");
    }
}
