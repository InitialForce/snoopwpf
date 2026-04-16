namespace SnoopWPF.Agent.Tests.Tools;

using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Tools;

/// <summary>
/// BEAD-012: Tests for <see cref="ErrorMapping.ToMcpException"/> — all error codes,
/// SCREAMING_SNAKE_CASE conversion, and canonical suggestion strings.
/// </summary>
[TestFixture]
public class ErrorMappingTests
{
    // ── ToScreamingSnakeCase conversion ──────────────────────────────────────────

    [TestCase("NodeNotFound", "NODE_NOT_FOUND")]
    [TestCase("DispatcherBusy", "DISPATCHER_BUSY")]
    [TestCase("OperationTimedOut", "OPERATION_TIMED_OUT")]
    [TestCase("PropertyReadOnly", "PROPERTY_READ_ONLY")]
    [TestCase("TypeConversionFailed", "TYPE_CONVERSION_FAILED")]
    [TestCase("UnsupportedPropertyType", "UNSUPPORTED_PROPERTY_TYPE")]
    [TestCase("MutationDisabled", "MUTATION_DISABLED")]
    [TestCase("PropertyRedacted", "PROPERTY_REDACTED")]
    [TestCase("SessionNotFound", "SESSION_NOT_FOUND")]
    [TestCase("ProtocolMismatch", "PROTOCOL_MISMATCH")]
    [TestCase("ElementNotRenderable", "ELEMENT_NOT_RENDERABLE")]
    public void ToScreamingSnakeCase_ConvertsCorrectly(string pascalCase, string expected)
    {
        var result = ErrorMapping.ToScreamingSnakeCase(pascalCase);
        Assert.That(result, Is.EqualTo(expected));
    }

    // ── ToMcpException — all 11 error codes ─────────────────────────────────────

    [Test]
    public void NodeNotFound_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.NodeNotFound, "Node 0:5 not found");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[NODE_NOT_FOUND]"));
        Assert.That(mcp.Message, Does.Contain("Suggestion:"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.NodeNotFound));
    }

    [Test]
    public void DispatcherBusy_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.DispatcherBusy, "Dispatcher is busy");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[DISPATCHER_BUSY]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.DispatcherBusy));
    }

    [Test]
    public void OperationTimedOut_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[OPERATION_TIMED_OUT]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.OperationTimedOut));
    }

    [Test]
    public void PropertyReadOnly_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.PropertyReadOnly, "Property is read-only");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[PROPERTY_READ_ONLY]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.PropertyReadOnly));
    }

    [Test]
    public void TypeConversionFailed_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.TypeConversionFailed, "Cannot convert value");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[TYPE_CONVERSION_FAILED]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.TypeConversionFailed));
    }

    [Test]
    public void UnsupportedPropertyType_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.UnsupportedPropertyType, "Type not supported");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[UNSUPPORTED_PROPERTY_TYPE]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.UnsupportedPropertyType));
    }

    [Test]
    public void MutationDisabled_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.MutationDisabled, "Mutation is disabled");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[MUTATION_DISABLED]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.MutationDisabled));
    }

    [Test]
    public void PropertyRedacted_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.PropertyRedacted, "Property is redacted");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[PROPERTY_REDACTED]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.PropertyRedacted));
    }

    [Test]
    public void SessionNotFound_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.SessionNotFound, "No active session");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[SESSION_NOT_FOUND]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.SessionNotFound));
    }

    [Test]
    public void ProtocolMismatch_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.ProtocolMismatch, "Version mismatch");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[PROTOCOL_MISMATCH]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.ProtocolMismatch));
    }

    [Test]
    public void ElementNotRenderable_ProducesCorrectCode_AndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.ElementNotRenderable, "Element has zero size");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain("[ELEMENT_NOT_RENDERABLE]"));
        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.ElementNotRenderable));
    }

    // ── Message format ───────────────────────────────────────────────────────────

    [Test]
    public void MessageFormat_StartsWithCode_ContainsMessageAndSuggestion()
    {
        var ex = new SnoopException(SnoopErrorCode.NodeNotFound, "The node is gone");
        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.StartWith("[NODE_NOT_FOUND]"));
        Assert.That(mcp.Message, Does.Contain("The node is gone"));
        Assert.That(mcp.Message, Does.Contain("\n\nSuggestion:"));
    }

    [Test]
    public void InnerException_IsPreserved()
    {
        var original = new SnoopException(SnoopErrorCode.OperationTimedOut, "Timed out");
        var mcp = ErrorMapping.ToMcpException(original);

        Assert.That(mcp.InnerException, Is.SameAs(original));
    }

    // ── Canonical suggestion overrides custom suggestions ────────────────────────

    [Test]
    public void CanonicalSuggestion_TakesPrecedenceOverCustomSuggestions()
    {
        var ex = new SnoopException(
            SnoopErrorCode.NodeNotFound,
            "Custom message",
            suggestions: new[] { "custom suggestion" });

        var mcp = ErrorMapping.ToMcpException(ex);

        Assert.That(mcp.Message, Does.Contain(SnoopSuggestions.NodeNotFound));
    }

    // ── Suggestion is always present for all canonical codes ─────────────────────

    [Test]
    public void AllCanonicalCodes_HaveSuggestionInMessage()
    {
        var codes = new[]
        {
            SnoopErrorCode.NodeNotFound,
            SnoopErrorCode.DispatcherBusy,
            SnoopErrorCode.OperationTimedOut,
            SnoopErrorCode.PropertyReadOnly,
            SnoopErrorCode.TypeConversionFailed,
            SnoopErrorCode.UnsupportedPropertyType,
            SnoopErrorCode.MutationDisabled,
            SnoopErrorCode.PropertyRedacted,
            SnoopErrorCode.SessionNotFound,
            SnoopErrorCode.ProtocolMismatch,
            SnoopErrorCode.ElementNotRenderable,
        };

        foreach (var code in codes)
        {
            var ex = new SnoopException(code, "Test message");
            var mcp = ErrorMapping.ToMcpException(ex);
            Assert.That(mcp.Message, Does.Contain("Suggestion:"), $"Code {code} should produce a suggestion");
        }
    }
}
