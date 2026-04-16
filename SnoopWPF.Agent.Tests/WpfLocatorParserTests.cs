namespace SnoopWPF.Agent.Tests;

using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

[TestFixture]
public class WpfLocatorParserTests
{
    // ── AutomationId form ─────────────────────────────────────────────────

    [Test]
    public void AutomationId_RoundTrip()
    {
        const string raw = "automationId=StartButton";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.AutomationId));
        Assert.That(loc.Value, Is.EqualTo("StartButton"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
        Assert.That(loc.Qualifier, Is.Null);
        Assert.That(loc.QualifierValue, Is.Null);
    }

    [Test]
    public void AutomationId_WithSpecialChars()
    {
        const string raw = "automationId=My_Button-1";
        var loc = WpfLocatorParser.Parse(raw);
        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.AutomationId));
        Assert.That(loc.Value, Is.EqualTo("My_Button-1"));
    }

    // ── ViewModel form ────────────────────────────────────────────────────

    [Test]
    public void ViewModel_MinimalRoundTrip()
    {
        const string raw = "viewModel=SessionVm";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.ViewModel));
        Assert.That(loc.Value, Is.EqualTo("SessionVm"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
        Assert.That(loc.Qualifier, Is.Null);
        Assert.That(loc.QualifierValue, Is.Null);
    }

    [Test]
    public void ViewModel_WithPropertyAndValue_RoundTrip()
    {
        const string raw = "viewModel=SessionVm, property=IsActive, value=true";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.ViewModel));
        Assert.That(loc.Value, Is.EqualTo("SessionVm"));
        Assert.That(loc.Qualifier, Is.EqualTo("IsActive"));
        Assert.That(loc.QualifierValue, Is.EqualTo("true"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
    }

    [Test]
    public void ViewModel_WithPropertyOnly_RoundTrip()
    {
        const string raw = "viewModel=OrderVm, property=Status";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.ViewModel));
        Assert.That(loc.Value, Is.EqualTo("OrderVm"));
        Assert.That(loc.Qualifier, Is.EqualTo("Status"));
        Assert.That(loc.QualifierValue, Is.Null);
    }

    // ── TypeName form ─────────────────────────────────────────────────────

    [Test]
    public void TypeName_WithoutName_RoundTrip()
    {
        const string raw = "type=Button";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.TypeName));
        Assert.That(loc.Value, Is.EqualTo("Button"));
        Assert.That(loc.Qualifier, Is.Null);
        Assert.That(loc.Raw, Is.EqualTo(raw));
    }

    [Test]
    public void TypeName_WithName_RoundTrip()
    {
        const string raw = "type=Button, name=Start";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.TypeName));
        Assert.That(loc.Value, Is.EqualTo("Button"));
        Assert.That(loc.Qualifier, Is.EqualTo("Start"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
    }

    // ── Path form ─────────────────────────────────────────────────────────

    [Test]
    public void Path_SingleSegment_RoundTrip()
    {
        const string raw = @"path=Window";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.Path));
        Assert.That(loc.Value, Is.EqualTo("Window"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
    }

    [Test]
    public void Path_MultiSegment_RoundTrip()
    {
        const string raw = @"path=Window\Grid\StackPanel\Button";
        var loc = WpfLocatorParser.Parse(raw);

        Assert.That(loc.Form, Is.EqualTo(WpfLocatorForm.Path));
        Assert.That(loc.Value, Is.EqualTo(@"Window\Grid\StackPanel\Button"));
        Assert.That(loc.Raw, Is.EqualTo(raw));
    }

    // ── Error cases ───────────────────────────────────────────────────────

    [Test]
    public void UnknownKey_Throws()
    {
        const string raw = "unknownKey=foo";
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void AutomationId_WithExtraKey_Throws()
    {
        const string raw = "automationId=StartButton, extra=bad";
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void ViewModel_WithUnknownKey_Throws()
    {
        const string raw = "viewModel=SessionVm, unknown=oops";
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void TypeName_WithUnknownKey_Throws()
    {
        const string raw = "type=Button, color=red";
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void Path_WithExtraKey_Throws()
    {
        const string raw = @"path=Window\Grid, extra=bad";
        // The ", extra=bad" splits off as a second segment — "extra" is unknown.
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void OversizeString_Throws()
    {
        var raw = "automationId=" + new string('x', 2048);
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void ExactlyAtLimit_DoesNotThrow()
    {
        // 2048 chars total; key is 13 chars ("automationId="), value is 2035 chars.
        var raw = "automationId=" + new string('x', 2048 - 13);
        Assert.That(raw.Length, Is.EqualTo(2048));
        Assert.DoesNotThrow(() => WpfLocatorParser.Parse(raw));
    }

    [Test]
    public void NullInput_Throws()
    {
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(null!));
    }

    [Test]
    public void EmptyString_NoKnownKey_Throws()
    {
        Assert.Throws<LocatorParseException>(() => WpfLocatorParser.Parse(string.Empty));
    }

    // ── Raw preservation ──────────────────────────────────────────────────

    [Test]
    public void RawIsPreservedVerbatim_ForAllForms()
    {
        string[] raws =
        {
            "automationId=Btn",
            "viewModel=Vm",
            "type=TextBox",
            @"path=W\G",
        };

        foreach (var raw in raws)
        {
            var loc = WpfLocatorParser.Parse(raw);
            Assert.That(loc.Raw, Is.EqualTo(raw), $"Raw not preserved for: {raw}");
        }
    }
}
