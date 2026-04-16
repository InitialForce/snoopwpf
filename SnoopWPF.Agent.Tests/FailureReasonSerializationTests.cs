namespace SnoopWPF.Agent.Tests;

using System.Text.Json;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Verifies that <see cref="FailureReason"/> serialises as SCREAMING_SNAKE_CASE
/// and round-trips correctly through <see cref="ToolSerializerOptions.Default"/>.
/// Acceptance criterion: PRD §7.2.
/// </summary>
[TestFixture]
public class FailureReasonSerializationTests
{
    // ── Serialise: each value must produce the expected SCREAMING_SNAKE_CASE string ──

    [TestCase(FailureReason.ElementNotFound,        "\"ELEMENT_NOT_FOUND\"")]
    [TestCase(FailureReason.ElementNotVisible,       "\"ELEMENT_NOT_VISIBLE\"")]
    [TestCase(FailureReason.ElementNotEnabled,       "\"ELEMENT_NOT_ENABLED\"")]
    [TestCase(FailureReason.CannotExecuteCommand,    "\"CANNOT_EXECUTE_COMMAND\"")]
    [TestCase(FailureReason.AutomationDisabled,      "\"AUTOMATION_DISABLED\"")]
    [TestCase(FailureReason.MutationDisabled,        "\"MUTATION_DISABLED\"")]
    [TestCase(FailureReason.TierMismatch,            "\"TIER_MISMATCH\"")]
    [TestCase(FailureReason.StateUnchanged,          "\"STATE_UNCHANGED\"")]
    [TestCase(FailureReason.LocatorAmbiguous,        "\"LOCATOR_AMBIGUOUS\"")]
    [TestCase(FailureReason.DispatcherBusy,          "\"DISPATCHER_BUSY\"")]
    [TestCase(FailureReason.ElementOutsideViewport,  "\"ELEMENT_OUTSIDE_VIEWPORT\"")]
    [TestCase(FailureReason.PatternNotSupported,     "\"PATTERN_NOT_SUPPORTED\"")]
    [TestCase(FailureReason.TargetNotRunning,        "\"TARGET_NOT_RUNNING\"")]
    public void Serialises_AsScreamingSnakeCase(FailureReason reason, string expectedJson)
    {
        var json = JsonSerializer.Serialize(reason, ToolSerializerOptions.Default);
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    // ── Round-trip: deserialise back to the original enum value ─────────────────────

    [TestCase(FailureReason.ElementNotFound,        "\"ELEMENT_NOT_FOUND\"")]
    [TestCase(FailureReason.ElementNotVisible,       "\"ELEMENT_NOT_VISIBLE\"")]
    [TestCase(FailureReason.ElementNotEnabled,       "\"ELEMENT_NOT_ENABLED\"")]
    [TestCase(FailureReason.CannotExecuteCommand,    "\"CANNOT_EXECUTE_COMMAND\"")]
    [TestCase(FailureReason.AutomationDisabled,      "\"AUTOMATION_DISABLED\"")]
    [TestCase(FailureReason.MutationDisabled,        "\"MUTATION_DISABLED\"")]
    [TestCase(FailureReason.TierMismatch,            "\"TIER_MISMATCH\"")]
    [TestCase(FailureReason.StateUnchanged,          "\"STATE_UNCHANGED\"")]
    [TestCase(FailureReason.LocatorAmbiguous,        "\"LOCATOR_AMBIGUOUS\"")]
    [TestCase(FailureReason.DispatcherBusy,          "\"DISPATCHER_BUSY\"")]
    [TestCase(FailureReason.ElementOutsideViewport,  "\"ELEMENT_OUTSIDE_VIEWPORT\"")]
    [TestCase(FailureReason.PatternNotSupported,     "\"PATTERN_NOT_SUPPORTED\"")]
    [TestCase(FailureReason.TargetNotRunning,        "\"TARGET_NOT_RUNNING\"")]
    public void RoundTrip_Deserialises_ToOriginalValue(FailureReason expected, string json)
    {
        var actual = JsonSerializer.Deserialize<FailureReason>(json, ToolSerializerOptions.Default);
        Assert.That(actual, Is.EqualTo(expected));
    }

    // ── Not-integer: JSON must NOT contain a bare integer for any value ──────────────

    [Test]
    public void AllValues_SerialiseAsString_NotInteger()
    {
        foreach (FailureReason reason in System.Enum.GetValues(typeof(FailureReason)))
        {
            var json = JsonSerializer.Serialize(reason, ToolSerializerOptions.Default);
            Assert.That(json, Does.StartWith("\""),
                $"FailureReason.{reason} must serialise as a JSON string, not integer. Got: {json}");
        }
    }
}
