namespace SnoopWPF.Agent.Tests.Infrastructure;

using System.Net;
using System.Threading;
using NUnit.Framework;
using Snoop.Infrastructure;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// FX-C4 (bd-151): DtoProjection.ToPropertyDto must call
/// RedactionFilter.IsStructurallySensitive before accessing prop.StringValue,
/// catching sensitive runtime types regardless of property name or enableRedaction flag.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class DtoProjectionStructuralRedactionTests
{
    // ─── NetworkCredential under innocuous name ──────────────────────────────

    [Test]
    public void ToPropertyDto_TagHoldingNetworkCredential_IsRedacted()
    {
        // "Tag" does not match any sensitive keyword — only the runtime type triggers redaction.
        var cred = new NetworkCredential("user", "p@ssw0rd");
        var prop = new PropertyInformation(new object(), null, "Tag", cred);

        var dto = DtoProjection.ToPropertyDto(prop, enableRedaction: true);

        Assert.That(dto.Value, Is.EqualTo("[REDACTED]"),
            "A NetworkCredential value held in a Tag property must be structurally redacted.");
        Assert.That(dto.IsRedacted, Is.True);
    }

    [Test]
    public void ToPropertyDto_TagHoldingNetworkCredential_IsRedactedEvenWhenEnableRedactionFalse()
    {
        // Structural sensitivity is unconditional — bypassing enableRedaction must not leak.
        var cred = new NetworkCredential("user", "secret");
        var prop = new PropertyInformation(new object(), null, "Tag", cred);

        var dto = DtoProjection.ToPropertyDto(prop, enableRedaction: false);

        Assert.That(dto.Value, Is.EqualTo("[REDACTED]"),
            "Structural redaction must fire even when enableRedaction is false.");
        Assert.That(dto.IsRedacted, Is.True);
    }

    // ─── [Sensitive]-typed value ─────────────────────────────────────────────

    [Test]
    public void ToPropertyDto_SensitiveTypedValue_IsRedacted()
    {
        // Property named "Data" holds a [Sensitive]-tagged runtime type.
        var sensitive = new SensitiveValueStub();
        var prop = new PropertyInformation(new object(), null, "Data", sensitive);

        var dto = DtoProjection.ToPropertyDto(prop, enableRedaction: true);

        Assert.That(dto.Value, Is.EqualTo("[REDACTED]"),
            "A [Sensitive]-attributed runtime value must be structurally redacted.");
        Assert.That(dto.IsRedacted, Is.True);
    }

    // ─── Innocent value must NOT be marked as redacted ──────────────────────

    [Test]
    public void ToPropertyDto_InnocentValue_IsRedactedFalse()
    {
        // A plain string named "Background" must not be flagged as redacted.
        // We assert only IsRedacted==false here; the actual string representation
        // depends on how PropertyInformation serialises the Value DP which is
        // exercised separately in PropertyInformationTests.
        var prop = new PropertyInformation(new object(), null, "Background", "White");

        var dto = DtoProjection.ToPropertyDto(prop, enableRedaction: true);

        Assert.That(dto.IsRedacted, Is.False,
            "An innocent property must not be structurally or keyword-redacted.");
    }

    // ─── Helper types ────────────────────────────────────────────────────────

    [Sensitive]
    private sealed class SensitiveValueStub
    {
        public override string ToString() => "LEAKED_SECRET";
    }
}
