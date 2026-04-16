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

    // ── FX-N3: Gap 5 — matrix cells missing from original coverage ─────────────

    /// <summary>
    /// CoLocated mode must not clobber EnableAutomation=true supplied by the caller.
    /// (CoLocated is an owned application — caller owns policy, no forced overrides.)
    /// </summary>
    [Test]
    public void Create_CoLocated_PreservesEnableAutomation()
    {
        var opts = new SnoopAgentOptions { EnableAutomation = true };
        var policy = SessionPolicy.Create(SessionMode.CoLocated, opts);
        Assert.That(policy.EnableAutomation, Is.True,
            "CoLocated must pass EnableAutomation=true through unchanged (FX-N3 gap 5).");
    }

    /// <summary>
    /// Brokered mode must not clobber EnableMutation=true supplied by the caller.
    /// Brokered is an owned-app session; caller owns the policy.
    /// </summary>
    [Test]
    public void Create_Brokered_PreservesEnableMutation()
    {
        var opts = new SnoopAgentOptions { EnableMutation = true };
        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);
        Assert.That(policy.EnableMutation, Is.True,
            "Brokered must pass EnableMutation=true through unchanged (FX-N3 gap 5).");
    }

    /// <summary>
    /// Brokered mode must not clobber AllowSensitiveRetention=true supplied by the caller.
    /// Brokered is an owned-app session (not injection), so the clamp must NOT apply.
    /// </summary>
    [Test]
    public void Create_Brokered_PreservesAllowSensitiveRetention()
    {
        var opts = new SnoopAgentOptions { AllowSensitiveRetention = true };
        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);
        Assert.That(policy.AllowSensitiveRetention, Is.True,
            "Brokered is owned-app — AllowSensitiveRetention must not be clamped (FX-N3 gap 5).");
    }
}
