// SnoopWPF.Agent.Tests/JsonCompatibilityTests.cs
// FX6-C3: verify STJ case-sensitivity matches DCJS net462 behaviour.

namespace SnoopWPF.Agent.Tests;

using System.Text.Json;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Regression tests for FX6-C3: STJ vs DCJS case-sensitivity parity.
/// </summary>
/// <remarks>
/// On net462 the injection path uses DataContractJsonSerializer (DCJS), which is
/// case-sensitive — it matches properties by the <c>[DataMember(Name=...)]</c> value.
/// On net6+ the same DTOs are (de)serialized via STJ.  Setting
/// <c>PropertyNameCaseInsensitive = false</c> makes STJ refuse PascalCase keys
/// the same way DCJS would.
/// </remarks>
[TestFixture]
public class JsonCompatibilityTests
{
    // ── AgentJsonOptions.Framed is case-sensitive ─────────────────────────────

    [Test]
    public void AgentJsonOptions_Framed_CaseSensitiveIsSetToFalse()
    {
        // AgentJsonOptions.Framed must explicitly set PropertyNameCaseInsensitive = false.
        Assert.That(
            AgentJsonOptions.Framed.PropertyNameCaseInsensitive,
            Is.False,
            "AgentJsonOptions.Framed must be case-sensitive to match DCJS net462 behaviour.");
    }

    [Test]
    public void AgentJsonOptions_Server_CaseSensitiveIsSetToFalse()
    {
        Assert.That(
            AgentJsonOptions.Server.PropertyNameCaseInsensitive,
            Is.False,
            "AgentJsonOptions.Server must be case-sensitive to match DCJS net462 behaviour.");
    }

    // ── MixedCase input is rejected (FX6-C3 acceptance criterion) ────────────

    [Test]
    public void MixedCaseRejectedOnBothTfms()
    {
        // DCJS (net462) is case-sensitive.  STJ must also reject PascalCase keys
        // so that a client sending { "NodeId": "..." } fails consistently across TFMs.
        //
        // net8 test: STJ with PropertyNameCaseInsensitive=false must return null/default
        // for PascalCase key "NodeId" when DTO expects camelCase "nodeId".
        const string pascalCaseJson = "{\"NodeId\":\"test-123\",\"TypeName\":\"Button\",\"Name\":\"btn\",\"DataContextType\":null}";

        var options = AgentJsonOptions.Framed;
        var result = JsonSerializer.Deserialize<AncestorDto>(pascalCaseJson, options);

        // With case-sensitive options, PascalCase keys do NOT map to camelCase properties.
        // The DTO's NodeId property should remain the default (empty string) because
        // "NodeId" (Pascal) ≠ "nodeId" (camel) when case-sensitive.
        Assert.That(
            result!.NodeId,
            Is.EqualTo(string.Empty),
            "PascalCase 'NodeId' must not be mapped to 'nodeId' property when case-sensitive. " +
            "This ensures STJ behaviour matches DCJS net462 (FX6-C3).");
    }

    [Test]
    public void CamelCaseAccepted()
    {
        // camelCase keys must still be accepted correctly.
        const string camelCaseJson = "{\"nodeId\":\"test-123\",\"typeName\":\"Button\",\"name\":\"btn\",\"dataContextType\":null}";

        var options = AgentJsonOptions.Framed;
        var result = JsonSerializer.Deserialize<AncestorDto>(camelCaseJson, options);

        Assert.That(result!.NodeId, Is.EqualTo("test-123"),
            "camelCase 'nodeId' must be mapped correctly even with case-sensitive options.");
        Assert.That(result.TypeName, Is.EqualTo("Button"));
    }
}
