namespace SnoopWPF.Agent.Tools;

using System;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Converts <see cref="SnoopException"/> to <see cref="McpException"/> with a formatted message
/// containing the SCREAMING_SNAKE_CASE error code and the canonical suggestion from <see cref="SnoopSuggestions"/>.
/// </summary>
public static class ErrorMapping
{
    /// <summary>
    /// Converts a <see cref="SnoopException"/> to an <see cref="McpException"/> whose message
    /// is formatted as <c>"[CODE] message\n\nSuggestion: suggestion"</c>.
    /// The suggestion is taken from <see cref="SnoopSuggestions"/> if present, or from the
    /// exception's own <see cref="SnoopException.Suggestions"/> array as a fallback.
    /// </summary>
    public static McpException ToMcpException(SnoopException ex)
    {
        var code = ToScreamingSnakeCase(ex.Code.ToString());
        var suggestion = GetSuggestion(ex.Code)
            ?? (ex.Suggestions?.Length > 0 ? string.Join("; ", ex.Suggestions) : null);

        var message = suggestion is not null
            ? $"[{code}] {ex.Message}\n\nSuggestion: {suggestion}"
            : $"[{code}] {ex.Message}";

        return new McpException(message, ex);
    }

    /// <summary>
    /// Converts a <see cref="SnoopErrorCode"/> PascalCase enum value to SCREAMING_SNAKE_CASE.
    /// e.g. <c>NodeNotFound</c> → <c>NODE_NOT_FOUND</c>.
    /// </summary>
    public static string ToScreamingSnakeCase(string pascalCase)
        => Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1_$2").ToUpperInvariant();

    private static string? GetSuggestion(SnoopErrorCode code) => code switch
    {
        SnoopErrorCode.NodeNotFound => SnoopSuggestions.NodeNotFound,
        SnoopErrorCode.DispatcherBusy => SnoopSuggestions.DispatcherBusy,
        SnoopErrorCode.OperationTimedOut => SnoopSuggestions.OperationTimedOut,
        SnoopErrorCode.PropertyReadOnly => SnoopSuggestions.PropertyReadOnly,
        SnoopErrorCode.TypeConversionFailed => SnoopSuggestions.TypeConversionFailed,
        SnoopErrorCode.UnsupportedPropertyType => SnoopSuggestions.UnsupportedPropertyType,
        SnoopErrorCode.MutationDisabled => SnoopSuggestions.MutationDisabled,
        SnoopErrorCode.PropertyRedacted => SnoopSuggestions.PropertyRedacted,
        SnoopErrorCode.SessionNotFound => SnoopSuggestions.SessionNotFound,
        SnoopErrorCode.ProtocolMismatch => SnoopSuggestions.ProtocolMismatch,
        SnoopErrorCode.ElementNotRenderable => SnoopSuggestions.ElementNotRenderable,
        SnoopErrorCode.BlobNotFound => SnoopSuggestions.BlobNotFound,
        SnoopErrorCode.InvalidArgument => SnoopSuggestions.InvalidArgument,
        SnoopErrorCode.CursorMismatch => SnoopSuggestions.CursorMismatch,
        SnoopErrorCode.AgentDisposed => SnoopSuggestions.AgentDisposed,
        SnoopErrorCode.InvalidState => SnoopSuggestions.InvalidState,
        _ => null,
    };
}
