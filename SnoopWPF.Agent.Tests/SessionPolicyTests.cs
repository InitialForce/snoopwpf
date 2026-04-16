namespace SnoopWPF.Agent.Tests;

using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

[TestFixture]
public class SessionPolicyTests
{
    [Test]
    public void Create_Injection_ForcesRedactionTrue()
    {
        var opts = new SnoopAgentOptions { EnableRedaction = false };
        var policy = SessionPolicy.Create(SessionMode.Injection, opts);
        Assert.That(policy.EnableRedaction, Is.True, "MF-11: injection must force redaction");
    }

    [Test]
    public void Create_Injection_CapsMaxTierToL0ReadOnly()
    {
        var opts = new SnoopAgentOptions { MaxTier = InputTier.L1 };
        var policy = SessionPolicy.Create(SessionMode.Injection, opts);
        Assert.That(policy.MaxTier, Is.EqualTo(InputTier.L0ReadOnly));
    }

    [Test]
    public void Create_CoLocated_PreservesCallerOptions()
    {
        var opts = new SnoopAgentOptions
        {
            EnableRedaction = false,
            EnableMutation = true,
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            AllowSensitiveRetention = true,
        };
        var policy = SessionPolicy.Create(SessionMode.CoLocated, opts);
        Assert.That(policy.EnableRedaction, Is.False);
        Assert.That(policy.EnableMutation, Is.True);
        Assert.That(policy.MaxTier, Is.EqualTo(InputTier.L1));
        Assert.That(policy.EnableAutomation, Is.True);
        Assert.That(policy.AllowSensitiveRetention, Is.True);
    }

    [Test]
    public void Create_Injection_ForcesAllowSensitiveRetentionFalse()
    {
        var opts = new SnoopAgentOptions { AllowSensitiveRetention = true };
        var policy = SessionPolicy.Create(SessionMode.Injection, opts);
        Assert.That(policy.AllowSensitiveRetention, Is.False, "FX-C5: injection must clamp sensitive retention");
    }

    [Test]
    public void Create_Brokered_PreservesCallerOptions()
    {
        var opts = new SnoopAgentOptions
        {
            EnableRedaction = false,
            MaxTier = InputTier.L1,
        };
        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);
        Assert.That(policy.EnableRedaction, Is.False, "Brokered is owned-app — passes through caller's choice");
        Assert.That(policy.MaxTier, Is.EqualTo(InputTier.L1));
        Assert.That(policy.Mode, Is.EqualTo(SessionMode.Brokered));
    }
}
