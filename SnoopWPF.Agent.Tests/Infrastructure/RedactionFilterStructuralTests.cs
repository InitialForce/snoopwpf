namespace SnoopWPF.Agent.Tests.Infrastructure;

using System.Data.Common;
using System.Net;
using System.Security;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// MF-10 structural-redaction tests (bead M1-03).
/// Verifies that <see cref="RedactionFilter.IsStructurallySensitive"/> and
/// <see cref="RedactionFilter.Redact"/> block sensitive runtime types before
/// <c>ToString()</c> is ever called.
/// </summary>
[TestFixture]
public class RedactionFilterStructuralTests
{
    // -------------------------------------------------------------------------
    // IsStructurallySensitive — positive cases
    // -------------------------------------------------------------------------

    [Test]
    public void IsStructurallySensitive_SecureString_ReturnsTrue()
    {
        using var ss = new SecureString();
        ss.AppendChar('s');
        ss.AppendChar('e');
        ss.AppendChar('c');

        Assert.That(RedactionFilter.IsStructurallySensitive(ss), Is.True);
    }

    [Test]
    public void IsStructurallySensitive_NetworkCredential_ReturnsTrue()
    {
        var cred = new NetworkCredential("user", "p@ssw0rd");

        Assert.That(RedactionFilter.IsStructurallySensitive(cred), Is.True);
    }

    [Test]
    public void IsStructurallySensitive_DbConnectionStringBuilderSubtype_ReturnsTrue()
    {
        // FakeConnectionStringBuilder is a subtype of DbConnectionStringBuilder.
        // Calling ToString() on it would expose the full connection string including
        // password — the structural map must catch it first.
        var builder = new FakeConnectionStringBuilder();
        builder["Server"] = "myserver";
        builder["Password"] = "hunter2";

        Assert.That(RedactionFilter.IsStructurallySensitive(builder), Is.True);
    }

    [Test]
    public void IsStructurallySensitive_SensitiveAttributeMarkedType_ReturnsTrue()
    {
        var value = new SensitiveStub();

        Assert.That(RedactionFilter.IsStructurallySensitive(value), Is.True);
    }

    // -------------------------------------------------------------------------
    // IsStructurallySensitive — negative / control cases
    // -------------------------------------------------------------------------

    [Test]
    public void IsStructurallySensitive_PlainString_ReturnsFalse()
    {
        Assert.That(RedactionFilter.IsStructurallySensitive("innocuous"), Is.False);
    }

    [Test]
    public void IsStructurallySensitive_Null_ReturnsFalse()
    {
        Assert.That(RedactionFilter.IsStructurallySensitive(null), Is.False);
    }

    [Test]
    public void IsStructurallySensitive_InnocentClass_ReturnsFalse()
    {
        Assert.That(RedactionFilter.IsStructurallySensitive(new InnocentStub()), Is.False);
    }

    // -------------------------------------------------------------------------
    // Redact — structural short-circuit fires BEFORE ToString()
    // -------------------------------------------------------------------------

    [Test]
    public void Redact_DbConnectionStringBuilderSubtypeNamedFoo_IsRedacted()
    {
        // Property name "Foo" does NOT match any keyword — only the runtime type
        // triggers redaction, proving the structural check runs first (MF-10 sentinel).
        var builder = new FakeConnectionStringBuilder();
        builder["Server"] = "srv";
        builder["Password"] = "leaked";

        var result = RedactionFilter.Redact("Foo", null, builder);

        Assert.That(result, Is.EqualTo("[REDACTED]"),
            "DbConnectionStringBuilder subtype value must be redacted even with an innocuous property name.");
    }

    [Test]
    public void Redact_SensitiveAttributeType_IsRedacted()
    {
        var result = RedactionFilter.Redact("Foo", null, new SensitiveStub());

        Assert.That(result, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Redact_SecureString_IsRedacted()
    {
        using var ss = new SecureString();
        var result = RedactionFilter.Redact("Foo", null, ss);

        Assert.That(result, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Redact_NetworkCredential_IsRedacted()
    {
        var result = RedactionFilter.Redact("Foo", null, new NetworkCredential("u", "p"));

        Assert.That(result, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Redact_InnocentStringNamedFoo_ReturnsValue()
    {
        // Control: a plain string DP named "Foo" must NOT be redacted.
        var result = RedactionFilter.Redact("Foo", typeof(string), "hello");

        Assert.That(result, Is.EqualTo("hello"),
            "Innocent string-typed property named 'Foo' must not be redacted.");
    }

    // -------------------------------------------------------------------------
    // Helper types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimal concrete DbConnectionStringBuilder subtype used to verify that
    /// the structural map catches any subtype, not just the base class itself.
    /// Its ToString() would emit the raw connection string (including credentials),
    /// so the structural check MUST fire before ToString() is called.
    /// </summary>
    private sealed class FakeConnectionStringBuilder : DbConnectionStringBuilder
    {
    }

    [Sensitive]
    private sealed class SensitiveStub
    {
        public override string ToString() => "SECRET_CONTENTS";
    }

    private sealed class InnocentStub
    {
        public override string ToString() => "innocent";
    }
}
