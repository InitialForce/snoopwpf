namespace SnoopWPF.Agent.Tools;

using System;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Wraps tool-handler delegates to ensure that all exception types are mapped to
/// <see cref="McpException"/> before reaching the MCP transport layer.  Without this
/// wrapper, <see cref="ObjectDisposedException"/>, <see cref="InvalidOperationException"/>,
/// and <see cref="AggregateException"/> escape as raw CLR type names — breaking the LLM's
/// error-recovery heuristics (FX6-A4).
///
/// <para>
/// Mapping rules:
/// <list type="bullet">
/// <item><see cref="ObjectDisposedException"/> → <see cref="SnoopErrorCode.AgentDisposed"/></item>
/// <item><see cref="InvalidOperationException"/> → <see cref="SnoopErrorCode.InvalidState"/></item>
/// <item><see cref="AggregateException"/> → unwrap first inner exception and recurse</item>
/// <item><see cref="SnoopException"/> → delegate to <see cref="ErrorMapping.ToMcpException"/></item>
/// <item><see cref="McpException"/> → rethrow as-is (already mapped)</item>
/// <item>All other exceptions → rethrow (unhandled exceptions indicate programming errors)</item>
/// </list>
/// </para>
/// </summary>
public static class ToolExceptionMapper
{
    /// <summary>
    /// Executes <paramref name="handler"/> and maps any thrown exception to
    /// <see cref="McpException"/> using the rules defined on <see cref="ToolExceptionMapper"/>.
    /// </summary>
    /// <param name="handler">The async tool-handler delegate returning a string result.</param>
    /// <returns>The string result of the handler.</returns>
    /// <exception cref="McpException">
    /// Thrown when <paramref name="handler"/> throws an exception that maps to a known error code.
    /// </exception>
    public static async Task<string> Wrap(Func<Task<string>> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (McpException)
        {
            // Already mapped — pass through.
            throw;
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
        catch (ObjectDisposedException ex)
        {
            // FX6-A4: map ODE → AgentDisposed.
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.AgentDisposed,
                $"The SnoopInspector was disposed while executing the tool: {ex.ObjectName}. " +
                "The agent session has ended.",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.AgentDisposed }));
        }
        catch (InvalidOperationException ex)
        {
            // FX6-A4: map IOE → InvalidState.
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidState,
                $"The operation is not valid in the current state: {ex.Message}",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.InvalidState }));
        }
        catch (AggregateException aex)
        {
            // FX6-A4: unwrap AggregateException and recurse on first inner exception.
            throw MapAggregateException(aex);
        }
    }

    /// <summary>
    /// Overload for tool handlers that return <see cref="CallToolResult"/>.
    /// </summary>
    public static async Task<CallToolResult> WrapCallToolResult(Func<Task<CallToolResult>> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (McpException)
        {
            throw;
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
        catch (ObjectDisposedException ex)
        {
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.AgentDisposed,
                $"The SnoopInspector was disposed while executing the tool: {ex.ObjectName}. " +
                "The agent session has ended.",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.AgentDisposed }));
        }
        catch (InvalidOperationException ex)
        {
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidState,
                $"The operation is not valid in the current state: {ex.Message}",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.InvalidState }));
        }
        catch (AggregateException aex)
        {
            throw MapAggregateException(aex);
        }
    }

    private static Exception MapAggregateException(AggregateException aex)
    {
        var inner = aex.InnerException ?? aex;

        if (inner is McpException mcpInner)
        {
            return mcpInner;
        }

        if (inner is SnoopException snoopInner)
        {
            return ErrorMapping.ToMcpException(snoopInner);
        }

        if (inner is ObjectDisposedException odeInner)
        {
            return ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.AgentDisposed,
                $"The SnoopInspector was disposed while executing the tool: {odeInner.ObjectName}. " +
                "The agent session has ended.",
                innerException: odeInner,
                suggestions: new[] { SnoopSuggestions.AgentDisposed }));
        }

        if (inner is InvalidOperationException ioeInner)
        {
            return ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidState,
                $"The operation is not valid in the current state: {ioeInner.Message}",
                innerException: ioeInner,
                suggestions: new[] { SnoopSuggestions.InvalidState }));
        }

        // Unrecognised inner exception — rethrow original AggregateException.
        return aex;
    }
}
