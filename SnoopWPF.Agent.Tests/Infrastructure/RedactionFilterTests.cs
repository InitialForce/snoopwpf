namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Security;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Infrastructure;

[TestFixture]
public class RedactionFilterTests
{
    // --- Sensitive keyword tests (all 21 keywords from spec) ---

    [TestCase("Password")]
    [TestCase("password")]
    [TestCase("PASSWORD")]
    [TestCase("UserPassword")]
    [TestCase("PasswordHash")]
    public void IsRedacted_Password_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Passwd")]
    [TestCase("myPasswd")]
    public void IsRedacted_Passwd_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Pwd")]
    [TestCase("OldPwd")]
    public void IsRedacted_Pwd_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Secret")]
    [TestCase("ClientSecret")]
    [TestCase("secretKey")]
    public void IsRedacted_Secret_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("ApiKey")]
    [TestCase("APIKEY")]
    [TestCase("myApiKey")]
    public void IsRedacted_ApiKey_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("ConnectionString")]
    [TestCase("connectionstring")]
    [TestCase("DbConnectionString")]
    public void IsRedacted_ConnectionString_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("ConnStr")]
    [TestCase("connstr")]
    public void IsRedacted_ConnStr_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Credential")]
    [TestCase("NetworkCredential")]
    [TestCase("UserCredentials")]
    public void IsRedacted_Credential_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("PrivateKey")]
    [TestCase("privatekey")]
    public void IsRedacted_PrivateKey_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("SharedKey")]
    [TestCase("sharedkey")]
    public void IsRedacted_SharedKey_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Cookie")]
    [TestCase("AuthCookie")]
    [TestCase("COOKIE_VALUE")]
    public void IsRedacted_Cookie_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("SessionKey")]
    [TestCase("sessionkey")]
    public void IsRedacted_SessionKey_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("Authorization")]
    [TestCase("authorization")]
    [TestCase("AuthorizationHeader")]
    public void IsRedacted_Authorization_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("AuthToken")]
    [TestCase("authtoken")]
    public void IsRedacted_AuthToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("AuthKey")]
    [TestCase("authkey")]
    public void IsRedacted_AuthKey_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("AccessToken")]
    [TestCase("accesstoken")]
    public void IsRedacted_AccessToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("BearerToken")]
    [TestCase("bearertoken")]
    public void IsRedacted_BearerToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("RefreshToken")]
    [TestCase("refreshtoken")]
    public void IsRedacted_RefreshToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("SessionToken")]
    [TestCase("sessiontoken")]
    public void IsRedacted_SessionToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("SasToken")]
    [TestCase("sastoken")]
    public void IsRedacted_SasToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    [TestCase("JwtToken")]
    [TestCase("jwttoken")]
    public void IsRedacted_JwtToken_ReturnsTrue(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.True);
    }

    // --- Safe property names that should NOT be redacted ---

    [TestCase("Background")]
    [TestCase("Width")]
    [TestCase("Height")]
    [TestCase("Content")]
    [TestCase("IsEnabled")]
    [TestCase("FontSize")]
    [TestCase("Margin")]
    [TestCase("Visibility")]
    [TestCase("Text")]
    [TestCase("Title")]
    public void IsRedacted_SafeProperty_ReturnsFalse(string name)
    {
        Assert.That(RedactionFilter.IsRedacted(name, null), Is.False);
    }

    // --- Type-based redaction ---

    [Test]
    public void IsRedacted_SecureStringType_ReturnsTrue()
    {
        Assert.That(RedactionFilter.IsRedacted("SafeName", typeof(SecureString)), Is.True);
    }

    [Test]
    public void IsRedacted_NullPropertyName_ReturnsFalse()
    {
        Assert.That(RedactionFilter.IsRedacted(string.Empty, null), Is.False);
    }

    // --- Redact method tests ---

    [Test]
    public void Redact_SensitiveProp_ReturnsRedactedPlaceholder()
    {
        var result = RedactionFilter.Redact("Password", null, "hunter2");

        Assert.That(result, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Redact_SafeProp_ReturnsValueString()
    {
        var result = RedactionFilter.Redact("Background", null, "Red");

        Assert.That(result, Is.EqualTo("Red"));
    }

    [Test]
    public void Redact_SafePropNullValue_ReturnsEmptyString()
    {
        var result = RedactionFilter.Redact("Background", null, null);

        Assert.That(result, Is.EqualTo(string.Empty));
    }
}
