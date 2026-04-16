namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Collections.Generic;
using System.Runtime.Serialization;

/// <summary>
/// Canonical post-action response for mutation tools (PRD §7, FD-3).
/// Returned by <c>wpf_set_property</c> and all M2 act-tools.
/// </summary>
/// <remarks>
/// <c>stateChanged</c> is computed at serialization time by
/// <see cref="SnoopWPF.Agent.Engine.StateDelta.StateDeltaSerializationHook"/> (M1-11),
/// which reads the observable DP value after any re-entrant PropertyChangedCallback has
/// settled, per PRD §7.3 W3-C1.
/// </remarks>
[DataContract]
public sealed record StateDeltaDto
{
    /// <summary>Whether the operation succeeded.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; init; }

    /// <summary>Whether the targeted element is currently visible on screen.</summary>
    [DataMember(Name = "elementVisible")]
    public bool ElementVisible { get; init; }

    /// <summary>
    /// Whether the element's state actually changed as a result of the operation.
    /// Computed at serialization time by comparing the observable DP value after any
    /// re-entrant PropertyChangedCallback has settled (PRD §7.3 W3-C1, M1-11).
    /// </summary>
    [DataMember(Name = "stateChanged")]
    public bool StateChanged { get; init; }

    /// <summary>The element that currently holds keyboard focus, if determinable.</summary>
    [DataMember(Name = "currentFocus")]
    public WpfLocator? CurrentFocus { get; init; }

    /// <summary>
    /// Difference in tree-version between the pre-call and post-call snapshots.
    /// Zero when the tree structure did not change.
    /// </summary>
    [DataMember(Name = "treeVersionDelta")]
    public int TreeVersionDelta { get; init; }

    /// <summary>Actionability checks that failed (populated only on failure).</summary>
    [DataMember(Name = "actionabilityChecksFailed")]
    public List<string>? ActionabilityChecksFailed { get; init; }

    /// <summary>Machine-readable failure code (null on success).</summary>
    [DataMember(Name = "failureReason")]
    public FailureReason? FailureReason { get; init; }

    /// <summary>Machine-executable remediation suggestion (null when no suggestion applies).</summary>
    [DataMember(Name = "suggestion")]
    public SuggestionDto? Suggestion { get; init; }

    // ── Debug-only fields — omitted from the wire unless options.Debug == true ──

    /// <summary>Tree version before the call (debug only).</summary>
    [DataMember(Name = "treeVersionBefore", EmitDefaultValue = false)]
    public long? TreeVersionBefore { get; init; }

    /// <summary>Tree version after the call (debug only).</summary>
    [DataMember(Name = "treeVersionAfter", EmitDefaultValue = false)]
    public long? TreeVersionAfter { get; init; }

    /// <summary>Wall-clock milliseconds the operation took (debug only).</summary>
    [DataMember(Name = "elapsedMs", EmitDefaultValue = false)]
    public long? ElapsedMs { get; init; }

    /// <summary>Input tier selected by the strategy selector (debug only).</summary>
    [DataMember(Name = "chosenTier", EmitDefaultValue = false)]
    public InputTier? ChosenTier { get; init; }

    // ── Legacy compatibility fields ──────────────────────────────────────────────

    /// <summary>
    /// The property value before the operation was applied.
    /// Carried forward from <see cref="SetPropertyResultDto"/> for compatibility.
    /// </summary>
    [DataMember(Name = "previousValue", EmitDefaultValue = false)]
    public string? PreviousValue { get; init; }

    /// <summary>
    /// The property value after the operation was applied (success only).
    /// Carried forward from <see cref="SetPropertyResultDto"/> for compatibility.
    /// </summary>
    [DataMember(Name = "newValue", EmitDefaultValue = false)]
    public string? NewValue { get; init; }
}
