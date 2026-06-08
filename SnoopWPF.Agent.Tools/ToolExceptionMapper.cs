namespace SnoopWPF.Agent.Tools;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Diagnostics;

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
/// <item><see cref="LocatorParseException"/> → <see cref="SnoopErrorCode.InvalidArgument"/> (FX6-C2)</item>
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
    /// <remarks>
    /// Before invoking <paramref name="handler"/> a fresh <see cref="SnoopAgentContext"/> scope
    /// is opened.  After the handler returns successfully, accumulated warnings are drained and,
    /// when non-empty, injected into the returned JSON object as a top-level <c>warnings</c>
    /// array.  Each element is a string of the form <c>[CODE] message</c>.  When no warnings
    /// were emitted the JSON payload is returned unchanged.
    /// </remarks>
    /// <param name="handler">The async tool-handler delegate returning a string result.</param>
    /// <returns>The string result of the handler, optionally augmented with a <c>warnings</c> field.</returns>
    /// <exception cref="McpException">
    /// Thrown when <paramref name="handler"/> throws an exception that maps to a known error code.
    /// </exception>
    public static async Task<string> Wrap(Func<Task<string>> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        using var scope = SnoopAgentContext.BeginScope();

        try
        {
            var result = await handler().ConfigureAwait(false);
            return AttachWarnings(result, SnoopAgentContext.DrainWarnings());
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
        catch (LocatorParseException ex)
        {
            // FX6-C2: map locator parse errors → InvalidArgument so the LLM sees a
            // structured error code rather than a raw LocatorParseException class name.
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidArgument,
                $"Invalid locator syntax: {ex.Message}",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.InvalidArgument }));
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
    /// Injects accumulated <paramref name="warnings"/> into the JSON payload as a top-level
    /// <c>warnings</c> array (strings of the form <c>[CODE] message</c>).
    /// Returns <paramref name="json"/> unchanged when <paramref name="warnings"/> is empty or
    /// when the payload is not a JSON object (e.g. a plain string or array).
    /// </summary>
    internal static string AttachWarnings(string json, IReadOnlyList<AgentWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return json;
        }

        // Only augment JSON object payloads.  Non-object responses (rare) are returned
        // as-is to avoid breaking downstream deserialisers.
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (node is not JsonObject obj)
        {
            return json;
        }

        var warningStrings = new JsonArray();
        foreach (var w in warnings)
        {
            warningStrings.Add(JsonValue.Create($"[{w.Code}] {w.Message}"));
        }

        obj["warnings"] = warningStrings;
        return obj.ToJsonString(ToolSerializerOptions.Default);
    }

    /// <summary>
    /// Overload for tool handlers that return <see cref="CallToolResult"/>.
    /// </summary>
    /// <remarks>
    /// Before invoking <paramref name="handler"/> a fresh <see cref="SnoopAgentContext"/> scope
    /// is opened.  After the handler returns successfully, accumulated warnings are drained and,
    /// when non-empty, injected into the returned <see cref="CallToolResult.Content"/> as an
    /// additional <see cref="TextContentBlock"/> containing a JSON object with a top-level
    /// <c>warnings</c> array.  Each element is a string of the form <c>[CODE] message</c>.
    /// When no warnings were emitted the result is returned unchanged.
    /// </remarks>
    public static async Task<CallToolResult> WrapCallToolResult(Func<Task<CallToolResult>> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        using var scope = SnoopAgentContext.BeginScope();

        try
        {
            var result = await handler().ConfigureAwait(false);
            return AttachWarnings(result, SnoopAgentContext.DrainWarnings());
        }
        catch (McpException)
        {
            throw;
        }
        catch (SnoopException ex)
        {
            throw ErrorMapping.ToMcpException(ex);
        }
        catch (LocatorParseException ex)
        {
            // FX6-C2: map locator parse errors → InvalidArgument.
            throw ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidArgument,
                $"Invalid locator syntax: {ex.Message}",
                innerException: ex,
                suggestions: new[] { SnoopSuggestions.InvalidArgument }));
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

    /// <summary>
    /// Injects accumulated <paramref name="warnings"/> into the <see cref="CallToolResult"/>
    /// as an additional <see cref="TextContentBlock"/> with a top-level <c>warnings</c> JSON
    /// array.  Returns <paramref name="result"/> unchanged when <paramref name="warnings"/>
    /// is empty.
    /// </summary>
    internal static CallToolResult AttachWarnings(CallToolResult result, IReadOnlyList<AgentWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return result;
        }

        var warningStrings = new JsonArray();
        foreach (var w in warnings)
        {
            warningStrings.Add(JsonValue.Create($"[{w.Code}] {w.Message}"));
        }

        var warningsObj = new JsonObject { ["warnings"] = warningStrings };
        var warningsBlock = new TextContentBlock { Text = warningsObj.ToJsonString(ToolSerializerOptions.Default) };

        var content = new List<ContentBlock>(result.Content ?? new List<ContentBlock>()) { warningsBlock };
        return new CallToolResult { Content = content, IsError = result.IsError };
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

        if (inner is LocatorParseException lpeInner)
        {
            return ErrorMapping.ToMcpException(new SnoopException(
                SnoopErrorCode.InvalidArgument,
                $"Invalid locator syntax: {lpeInner.Message}",
                innerException: lpeInner,
                suggestions: new[] { SnoopSuggestions.InvalidArgument }));
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
