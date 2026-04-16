namespace SnoopWPF.Agent.Tests;

using System;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// M1-04: Belt-and-braces MF-11 enforcement tests for <see cref="SnoopInspector.EnforceInjectionRedaction"/>.
/// </summary>
[TestFixture]
public class InjectionRedactionEnforcementTests
{
    [Test]
    public void EnforceInjectionRedaction_InjectionWithRedactionDisabled_ThrowsInvalidOperationException()
    {
        // Fabricate a bypass policy: Injection mode but EnableRedaction = false.
        // Record `with` syntax bypasses SessionPolicy.Create, simulating a downstream bug.
        var validPolicy = SessionPolicy.Create(SessionMode.Injection, new SnoopAgentOptions());
        var bypassPolicy = validPolicy with { EnableRedaction = false };

        var ex = Assert.Throws<InvalidOperationException>(
            () => SnoopInspector.EnforceInjectionRedaction(bypassPolicy));

        Assert.That(ex!.Message, Does.Contain("MF-11"));
    }

    [Test]
    public void EnforceInjectionRedaction_InjectionWithRedactionEnabled_DoesNotThrow()
    {
        var policy = SessionPolicy.Create(SessionMode.Injection, new SnoopAgentOptions());

        // EnableRedaction is forced true by Create; must not throw.
        Assert.DoesNotThrow(() => SnoopInspector.EnforceInjectionRedaction(policy));
    }

    [Test]
    public void EnforceInjectionRedaction_CoLocatedWithRedactionDisabled_DoesNotThrow()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions { EnableRedaction = false });

        // CoLocated is owned-app mode; MF-11 does not apply.
        Assert.DoesNotThrow(() => SnoopInspector.EnforceInjectionRedaction(policy));
    }

    [Test]
    public void EnforceInjectionRedaction_BrokeredWithRedactionDisabled_DoesNotThrow()
    {
        var policy = SessionPolicy.Create(SessionMode.Brokered, new SnoopAgentOptions { EnableRedaction = false });

        // Brokered is owned-app mode; MF-11 does not apply.
        Assert.DoesNotThrow(() => SnoopInspector.EnforceInjectionRedaction(policy));
    }

    [Test]
    public void EnforceInjectionRedaction_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => SnoopInspector.EnforceInjectionRedaction(null!));
    }
}
