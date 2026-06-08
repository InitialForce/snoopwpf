namespace SnoopWPF.Agent.Tests.Diagnostics;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Diagnostics;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Tests for <see cref="ToolExceptionMapper.Wrap"/> warning-surface behaviour
/// and <see cref="ToolExceptionMapper.AttachWarnings"/> JSON injection.
/// </summary>
[TestFixture]
public class ToolExceptionMapperWarningsTests
{
    // ── AttachWarnings helper ────────────────────────────────────────────────────

    [Test]
    public void AttachWarnings_EmptyWarnings_ReturnsOriginalJson()
    {
        const string json = "{\"foo\":\"bar\"}";

        var result = ToolExceptionMapper.AttachWarnings(json, Array.Empty<AgentWarning>());

        Assert.That(result, Is.EqualTo(json));
    }

    [Test]
    public void AttachWarnings_SingleWarning_InjectsWarningsArray()
    {
        const string json = "{\"result\":42}";
        var warnings = new[]
        {
            new AgentWarning("CODE_A", "message a", null, DateTimeOffset.UtcNow),
        };

        var result = ToolExceptionMapper.AttachWarnings(json, warnings);

        var node = JsonNode.Parse(result)!;
        var warningsArr = node["warnings"]!.AsArray();
        Assert.That(warningsArr.Count, Is.EqualTo(1));
        Assert.That(warningsArr[0]!.GetValue<string>(), Is.EqualTo("[CODE_A] message a"));
    }

    [Test]
    public void AttachWarnings_MultipleWarnings_PreservesInsertionOrder()
    {
        const string json = "{\"ok\":true}";
        var warnings = new[]
        {
            new AgentWarning("W1", "first", null, DateTimeOffset.UtcNow),
            new AgentWarning("W2", "second", null, DateTimeOffset.UtcNow),
            new AgentWarning("W3", "third", null, DateTimeOffset.UtcNow),
        };

        var result = ToolExceptionMapper.AttachWarnings(json, warnings);

        var node = JsonNode.Parse(result)!;
        var arr = node["warnings"]!.AsArray();
        Assert.That(arr.Count, Is.EqualTo(3));
        Assert.That(arr[0]!.GetValue<string>(), Does.StartWith("[W1]"));
        Assert.That(arr[1]!.GetValue<string>(), Does.StartWith("[W2]"));
        Assert.That(arr[2]!.GetValue<string>(), Does.StartWith("[W3]"));
    }

    [Test]
    public void AttachWarnings_NonObjectJson_ReturnsOriginalUnchanged()
    {
        // Array payloads are not augmented.
        const string json = "[1,2,3]";
        var warnings = new[]
        {
            new AgentWarning("CODE", "msg", null, DateTimeOffset.UtcNow),
        };

        var result = ToolExceptionMapper.AttachWarnings(json, warnings);

        Assert.That(result, Is.EqualTo(json));
    }

    [Test]
    public void AttachWarnings_InvalidJson_ReturnsOriginalUnchanged()
    {
        const string json = "not json at all";
        var warnings = new[]
        {
            new AgentWarning("CODE", "msg", null, DateTimeOffset.UtcNow),
        };

        var result = ToolExceptionMapper.AttachWarnings(json, warnings);

        Assert.That(result, Is.EqualTo(json));
    }

    // ── Wrap integration ─────────────────────────────────────────────────────────

    [Test]
    public async Task Wrap_HandlerAddsWarning_AppearsInJsonResult()
    {
        var result = await ToolExceptionMapper.Wrap(async () =>
        {
            // Simulate a tool handler that accumulates a non-fatal warning.
            SnoopAgentContext.AddWarning("TOOL_WARN", "element not fully visible");
            await Task.Yield();
            return "{\"nodeId\":\"1:2\"}";
        });

        var node = JsonNode.Parse(result)!;
        var arr = node["warnings"]?.AsArray();

        Assert.That(arr, Is.Not.Null, "warnings field must be present when warnings were emitted");
        Assert.That(arr!.Count, Is.EqualTo(1));
        Assert.That(arr[0]!.GetValue<string>(), Is.EqualTo("[TOOL_WARN] element not fully visible"));
    }

    [Test]
    public async Task Wrap_HandlerAddsNoWarnings_NoWarningsFieldInResult()
    {
        var result = await ToolExceptionMapper.Wrap(() =>
            Task.FromResult("{\"nodeId\":\"1:2\"}"));

        var node = JsonNode.Parse(result)!;

        // warnings key must be absent when no warnings were added.
        Assert.That(node["warnings"], Is.Null, "warnings field must be absent when no warnings were emitted");
    }

    [Test]
    public async Task Wrap_ScopesAreIndependent_BetweenCalls()
    {
        // First call adds a warning.
        var result1 = await ToolExceptionMapper.Wrap(async () =>
        {
            SnoopAgentContext.AddWarning("CALL1_WARN", "first call warning");
            await Task.Yield();
            return "{\"call\":1}";
        });

        // Second call adds no warnings.
        var result2 = await ToolExceptionMapper.Wrap(() =>
            Task.FromResult("{\"call\":2}"));

        var node1 = JsonNode.Parse(result1)!;
        var node2 = JsonNode.Parse(result2)!;

        Assert.That(node1["warnings"]?.AsArray()?.Count, Is.EqualTo(1),
            "First call should have its warning.");
        Assert.That(node2["warnings"], Is.Null,
            "Second call must not inherit warnings from first call.");
    }

    // ── WrapCallToolResult integration ───────────────────────────────────────────

    [Test]
    public async Task WrapCallToolResult_HandlerAddsWarning_WarningAppearsInContentBlock()
    {
        var result = await ToolExceptionMapper.WrapCallToolResult(async () =>
        {
            SnoopAgentContext.AddWarning("CAPTURE_WARN", "partial occlusion detected");
            await Task.Yield();
            var textBlock = new TextContentBlock { Text = "{\"blobRef\":\"blob:x:1\"}" };
            return new CallToolResult { Content = new List<ContentBlock> { textBlock } };
        });

        // Handler produced 1 text block; WrapCallToolResult must append a warnings block.
        Assert.That(result.Content, Has.Count.EqualTo(2),
            "WrapCallToolResult must append a warnings content block when warnings are emitted.");

        var warningsBlock = result.Content[1] as TextContentBlock;
        Assert.That(warningsBlock, Is.Not.Null, "Appended block must be a TextContentBlock.");

        var node = JsonNode.Parse(warningsBlock!.Text)!;
        var arr = node["warnings"]?.AsArray();
        Assert.That(arr, Is.Not.Null, "warnings field must be present in the appended block.");
        Assert.That(arr!.Count, Is.EqualTo(1));
        Assert.That(arr[0]!.GetValue<string>(), Is.EqualTo("[CAPTURE_WARN] partial occlusion detected"));
    }

    [Test]
    public async Task WrapCallToolResult_HandlerAddsNoWarnings_ContentUnchanged()
    {
        var textBlock = new TextContentBlock { Text = "{\"blobRef\":\"blob:y:2\"}" };
        var expected = new CallToolResult { Content = new List<ContentBlock> { textBlock } };

        var result = await ToolExceptionMapper.WrapCallToolResult(() => Task.FromResult(expected));

        Assert.That(result, Is.SameAs(expected),
            "WrapCallToolResult must return the original result unchanged when no warnings are emitted.");
    }

    [Test]
    public async Task WrapCallToolResult_ScopesAreIndependent_BetweenCalls()
    {
        // First call adds a warning.
        var result1 = await ToolExceptionMapper.WrapCallToolResult(async () =>
        {
            SnoopAgentContext.AddWarning("WARN_A", "first");
            await Task.Yield();
            return new CallToolResult { Content = new List<ContentBlock> { new TextContentBlock { Text = "{\"call\":1}" } } };
        });

        // Second call adds no warnings.
        var result2 = await ToolExceptionMapper.WrapCallToolResult(() =>
        {
            return Task.FromResult(new CallToolResult { Content = new List<ContentBlock> { new TextContentBlock { Text = "{\"call\":2}" } } });
        });

        Assert.That(result1.Content, Has.Count.EqualTo(2),
            "First call must have the original block plus the warnings block.");
        Assert.That(result2.Content, Has.Count.EqualTo(1),
            "Second call must not inherit warnings from the first call.");
    }
}
