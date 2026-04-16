namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Exception thrown by SnoopWPF.Agent operations, carrying a structured error code, optional target node,
/// and optional suggestions for recovery.
/// </summary>
public sealed class SnoopException : Exception
{
    public SnoopErrorCode Code { get; }

    /// <summary>The nodeId or element identifier that was the target of the failed operation, if applicable.</summary>
    public string? TargetId { get; }

    public string[]? Suggestions { get; }

    public SnoopException(SnoopErrorCode code, string message, string? targetId = null, string[]? suggestions = null)
        : base(message)
    {
        this.Code = code;
        this.TargetId = targetId;
        this.Suggestions = suggestions;
    }

    public SnoopException(SnoopErrorCode code, string message, Exception innerException, string? targetId = null, string[]? suggestions = null)
        : base(message, innerException)
    {
        this.Code = code;
        this.TargetId = targetId;
        this.Suggestions = suggestions;
    }
}
