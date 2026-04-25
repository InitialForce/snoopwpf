namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// One atomic step in an action sequence consumed by <c>wpf_act_sequence</c>.
/// Server-side execution dispatches by <see cref="Type"/> to the corresponding L0/L1 primitive.
/// </summary>
[DataContract]
public sealed class ActionStepDto
{
    /// <summary>
    /// Action kind. Supported values:
    /// <c>click</c> (InvokePattern), <c>execute_command</c> (L0 ButtonBase.Command),
    /// <c>set_text</c> (TextBoxBase / PasswordBox), <c>double_click</c>.
    /// </summary>
    [DataMember(Name = "type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Node identifier of the target element.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Step-specific payload. Used by <c>set_text</c> (the text to set).
    /// Ignored by <c>click</c> / <c>execute_command</c> / <c>double_click</c>.
    /// </summary>
    [DataMember(Name = "value")]
    public string? Value { get; set; }
}

/// <summary>
/// Result of one step within an action sequence.
/// </summary>
[DataContract]
public sealed class ActionStepResultDto
{
    /// <summary>Index of this step within the original input list (0-based).</summary>
    [DataMember(Name = "index")]
    public int Index { get; set; }

    /// <summary>Echo of the originating step's type.</summary>
    [DataMember(Name = "type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Echo of the originating step's nodeId.</summary>
    [DataMember(Name = "nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary><see langword="true"/> when the underlying primitive returned <c>StateDelta.Success=true</c>.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; set; }

    /// <summary>The full <see cref="StateDeltaDto"/> from the underlying primitive.</summary>
    [DataMember(Name = "delta")]
    public StateDeltaDto? Delta { get; set; }

    /// <summary>
    /// Error code when the step failed (mirror of the SnoopErrorCode that would be raised
    /// by the equivalent single-tool call). Null on success.
    /// </summary>
    [DataMember(Name = "errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable error message when the step failed. Null on success.</summary>
    [DataMember(Name = "errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of <c>wpf_act_sequence</c>. Reports per-step outcome and overall completion.
/// </summary>
[DataContract]
public sealed class ActionSequenceResultDto
{
    /// <summary><see langword="true"/> when every step in the input list succeeded.</summary>
    [DataMember(Name = "allSucceeded")]
    public bool AllSucceeded { get; set; }

    /// <summary>
    /// Index of the first failing step, or <c>-1</c> when every step succeeded.
    /// Always equal to <c>steps.Count - 1</c> in <c>continueOnError=true</c> mode when nothing failed.
    /// </summary>
    [DataMember(Name = "stoppedAtIndex")]
    public int StoppedAtIndex { get; set; }

    /// <summary>Per-step result, in input order. Fewer entries than the input when stopOnError aborted early.</summary>
    [DataMember(Name = "steps")]
    public List<ActionStepResultDto> Steps { get; set; } = new List<ActionStepResultDto>();
}
