namespace SnoopWPF.Agent.Tests;

using SnoopWPF.Agent.Contracts;
using Xunit;

public class SessionPolicyTests
{
    [Fact]
    public void Create_Injection_ForcesRedactionTrue()
    {
        var opts = new SnoopAgentOptions { EnableRedaction = false };
        var policy = SessionPolicy.Create(SessionMode.Injection, opts);
        Assert.True(policy.EnableRedaction, "MF-11: injection must force redaction");
    }

    [Fact]
    public void Create_Injection_CapsMaxTierToL0ReadOnly()
    {
        var opts = new SnoopAgentOptions { MaxTier = InputTier.L1 };
        var policy = SessionPolicy.Create(SessionMode.Injection, opts);
        Assert.Equal(InputTier.L0ReadOnly, policy.MaxTier);
    }

    [Fact]
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
        Assert.False(policy.EnableRedaction);
        Assert.True(policy.EnableMutation);
        Assert.Equal(InputTier.L1, policy.MaxTier);
        Assert.True(policy.EnableAutomation);
        Assert.True(policy.AllowSensitiveRetention);
    }

    [Fact]
    public void Create_Brokered_PreservesCallerOptions()
    {
        var opts = new SnoopAgentOptions
        {
            EnableRedaction = false,
            MaxTier = InputTier.L1,
        };
        var policy = SessionPolicy.Create(SessionMode.Brokered, opts);
        Assert.False(policy.EnableRedaction, "Brokered is owned-app — passes through caller's choice");
        Assert.Equal(InputTier.L1, policy.MaxTier);
        Assert.Equal(SessionMode.Brokered, policy.Mode);
    }
}
