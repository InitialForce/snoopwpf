// SnoopWPF.Agent.Tests/Tools/ToolExceptionMapperTests.cs
// FX6-A4: acceptance tests for ToolExceptionMapper.

namespace SnoopWPF.Agent.Tests.Tools;

using System;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="ToolExceptionMapper"/> (FX6-A4).
/// Verifies that ODE, IOE, and AggregateException are correctly mapped to
/// <see cref="McpException"/> before reaching the MCP transport layer.
/// </summary>
[TestFixture]
public sealed class ToolExceptionMapperTests
{
    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): ObjectDisposedException → AgentDisposed
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_ObjectDisposedException_ThrowsMcpExceptionWithAgentDisposedCode()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw new ObjectDisposedException("SnoopInspector")));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"),
            "McpException message must contain AGENT_DISPOSED error code.");
    }

    [Test]
    public void Wrap_ObjectDisposedException_MessageContainsObjectName()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw new ObjectDisposedException("SnoopInspector", "Already disposed")));

        Assert.That(ex!.Message, Does.Contain("SnoopInspector"),
            "McpException message must contain the disposed object name.");
    }

    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): InvalidOperationException → InvalidState
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_InvalidOperationException_ThrowsMcpExceptionWithInvalidStateCode()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw new InvalidOperationException("Not in valid state")));

        Assert.That(ex!.Message, Does.Contain("INVALID_STATE"),
            "McpException message must contain INVALID_STATE error code.");
    }

    [Test]
    public void Wrap_InvalidOperationException_MessageContainsOriginalMessage()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw new InvalidOperationException("Not in valid state")));

        Assert.That(ex!.Message, Does.Contain("Not in valid state"),
            "McpException message must contain the original IOE message.");
    }

    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): AggregateException → unwrap inner and recurse
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_AggregateException_WrappingOde_ThrowsMcpExceptionWithAgentDisposedCode()
    {
        var ode = new ObjectDisposedException("SnoopInspector");
        var aex = new AggregateException(ode);

        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw aex));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"),
            "AggregateException wrapping ODE must be unwrapped to AGENT_DISPOSED.");
    }

    [Test]
    public void Wrap_AggregateException_WrappingIoe_ThrowsMcpExceptionWithInvalidStateCode()
    {
        var ioe = new InvalidOperationException("State problem");
        var aex = new AggregateException(ioe);

        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw aex));

        Assert.That(ex!.Message, Does.Contain("INVALID_STATE"),
            "AggregateException wrapping IOE must be unwrapped to INVALID_STATE.");
    }

    [Test]
    public void Wrap_AggregateException_WrappingSnoopException_ThrowsMcpExceptionWithSnoopCode()
    {
        var snoopEx = new SnoopException(SnoopErrorCode.NodeNotFound, "node gone");
        var aex = new AggregateException(snoopEx);

        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw aex));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"),
            "AggregateException wrapping SnoopException must be mapped via SnoopErrorCode.");
    }

    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): McpException passes through unchanged
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_McpException_PassesThroughUnchanged()
    {
        var original = new McpException("already mapped error");

        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw original));

        Assert.That(ex, Is.SameAs(original),
            "McpException must be rethrown as-is without re-wrapping.");
    }

    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): SnoopException → mapped via ErrorMapping
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_SnoopException_ThrowsMcpExceptionWithSnoopCode()
    {
        var snoopEx = new SnoopException(SnoopErrorCode.DispatcherBusy, "busy");

        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.Wrap(() =>
                throw snoopEx));

        Assert.That(ex!.Message, Does.Contain("DISPATCHER_BUSY"),
            "SnoopException must be mapped to McpException with its error code.");
    }

    // -------------------------------------------------------------------------
    // Wrap(Func<Task<string>>): successful handler returns result unchanged
    // -------------------------------------------------------------------------

    [Test]
    public async Task Wrap_SuccessfulHandler_ReturnsResult()
    {
        var result = await ToolExceptionMapper.Wrap(() => Task.FromResult("hello")).ConfigureAwait(false);

        Assert.That(result, Is.EqualTo("hello"),
            "A successful handler must return its result unchanged.");
    }

    // -------------------------------------------------------------------------
    // WrapCallToolResult: ObjectDisposedException → AgentDisposed
    // -------------------------------------------------------------------------

    [Test]
    public void WrapCallToolResult_ObjectDisposedException_ThrowsMcpException()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.WrapCallToolResult(() =>
                throw new ObjectDisposedException("SnoopInspector")));

        Assert.That(ex!.Message, Does.Contain("AGENT_DISPOSED"),
            "WrapCallToolResult must map ODE to AGENT_DISPOSED.");
    }

    // -------------------------------------------------------------------------
    // WrapCallToolResult: InvalidOperationException → InvalidState
    // -------------------------------------------------------------------------

    [Test]
    public void WrapCallToolResult_InvalidOperationException_ThrowsMcpException()
    {
        var ex = Assert.ThrowsAsync<McpException>(() =>
            ToolExceptionMapper.WrapCallToolResult(() =>
                throw new InvalidOperationException("bad state")));

        Assert.That(ex!.Message, Does.Contain("INVALID_STATE"),
            "WrapCallToolResult must map IOE to INVALID_STATE.");
    }

    // -------------------------------------------------------------------------
    // WrapCallToolResult: successful handler returns result
    // -------------------------------------------------------------------------

    [Test]
    public async Task WrapCallToolResult_SuccessfulHandler_ReturnsResult()
    {
        var expected = new CallToolResult { Content = new System.Collections.Generic.List<ContentBlock>() };

        var result = await ToolExceptionMapper.WrapCallToolResult(() => Task.FromResult(expected)).ConfigureAwait(false);

        Assert.That(result, Is.SameAs(expected),
            "WrapCallToolResult must return the handler result unchanged.");
    }

    // -------------------------------------------------------------------------
    // Null handler guard
    // -------------------------------------------------------------------------

    [Test]
    public void Wrap_NullHandler_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() =>
            ToolExceptionMapper.Wrap(null!));
    }

    [Test]
    public void WrapCallToolResult_NullHandler_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() =>
            ToolExceptionMapper.WrapCallToolResult(null!));
    }
}
