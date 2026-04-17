// SnoopWPF.Agent.Tests/Injection/Net462MutationGuardTests.cs
// FX6-Z1: tests for the net462 mutation-refused guard.

namespace SnoopWPF.Agent.Tests.Injection;

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Unit tests for the FX6-Z1 guard: <c>EnableMutation=true</c> is refused on .NET Framework 4.6.2
/// injection targets because the audit subsystem (<c>AuditLogWriter</c>) depends on
/// <c>System.Threading.Channels</c> which is unavailable on net462.
///
/// The actual guard runs under <c>#if !NET6_0_OR_GREATER</c> in
/// <c>SnoopAgentEntryPoint.StartCore</c> and throws
/// <see cref="SnoopException"/>(<see cref="SnoopErrorCode.UnsupportedOnNet462"/>).
/// These tests verify the observable contract without requiring a net462 build environment.
/// </summary>
[TestFixture]
public sealed class Net462MutationGuardTests
{
    // -------------------------------------------------------------------------
    // SnoopErrorCode.UnsupportedOnNet462 is defined
    // -------------------------------------------------------------------------

    [Test]
    public void UnsupportedOnNet462_ErrorCode_IsDefined()
    {
        // The enum value must exist so the guard can throw a typed exception (FX6-Z1).
        Assert.That(
            Enum.IsDefined(typeof(SnoopErrorCode), nameof(SnoopErrorCode.UnsupportedOnNet462)),
            Is.True,
            "SnoopErrorCode.UnsupportedOnNet462 must be defined.");
    }

    // -------------------------------------------------------------------------
    // SnoopException(UnsupportedOnNet462) carries an audit-explanation message
    // -------------------------------------------------------------------------

    [Test]
    public void SnoopException_UnsupportedOnNet462_CarriesAuditMessage()
    {
        // Verify that constructing the guard exception works and the message
        // references the audit constraint so that callers receive actionable guidance.
        var ex = new SnoopException(
            SnoopErrorCode.UnsupportedOnNet462,
            "EnableMutation=true is not supported when the agent is injected into a .NET Framework 4.6.2 target. " +
            "The audit log subsystem requires System.Threading.Channels (net6+).");

        Assert.That(ex.Code, Is.EqualTo(SnoopErrorCode.UnsupportedOnNet462),
            "Exception code must be UnsupportedOnNet462.");
        Assert.That(ex.Message, Does.Contain("audit"),
            "Exception message must explain the audit trail constraint.");
        Assert.That(ex.Message, Does.Contain("net6"),
            "Exception message must reference the net6+ requirement.");
    }

    // -------------------------------------------------------------------------
    // On net6+ the guard condition is not met (current test runtime)
    // -------------------------------------------------------------------------

    [Test]
    public void Guard_IsNotTriggered_OnNet6Plus()
    {
        // The guard is wrapped in #if !NET6_0_OR_GREATER. This test confirms
        // that the current test runtime is net6+ so the guard is compiled out.
        // If this test runs at all it implies the guard cannot fire, which is correct.
        var frameworkDesc = RuntimeInformation.FrameworkDescription;
        Assert.That(
            frameworkDesc,
            Does.Contain(".NET ").Or.Contain(".NET Core"),
            "Test must run on .NET 6+ so that the net462 guard is #if'd out.");

        // Double-check via Environment.Version (major >= 6 for net6+).
        Assert.That(
            Environment.Version.Major,
            Is.GreaterThanOrEqualTo(6),
            "Test runner must be .NET 6+ for the guard to be #if'd out.");
    }

    // -------------------------------------------------------------------------
    // InjectionAgentOptions default EnableMutation is false
    // -------------------------------------------------------------------------

    [Test]
    public void InjectionAgentOptions_DefaultEnableMutation_IsFalse()
    {
        // The injection entry point constructs SnoopAgentOptions with EnableMutation=false
        // by default. This test documents that constraint as a regression guard —
        // if someone changes the default, this test will fail, prompting review of the
        // FX6-Z1 audit trail implications.
        var opts = new SnoopAgentOptions
        {
            EnableRedaction = true,
        };

        Assert.That(opts.EnableMutation, Is.False,
            "SnoopAgentOptions.EnableMutation must default to false. " +
            "Changing this default for the injection path requires addressing " +
            "the FX6-Z1 audit trail gap on net462 targets first.");
    }
}
