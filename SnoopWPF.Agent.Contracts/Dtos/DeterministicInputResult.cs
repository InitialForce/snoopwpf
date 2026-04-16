namespace SnoopWPF.Agent.Contracts.Dtos;

using System.Runtime.Serialization;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Result returned by <see cref="IDeterministicInputStrategy.Invoke"/> after a single
/// strategy execution attempt.
/// </summary>
/// <remarks>Per FD-5 (PRD §4.4). Used by <c>InputStrategySelector</c> and act-tool handlers.</remarks>
[DataContract]
public sealed record DeterministicInputResult
{
    /// <summary>Whether the strategy invocation succeeded.</summary>
    [DataMember(Name = "success")]
    public bool Success { get; init; }

    /// <summary>
    /// Reason for failure when <see cref="Success"/> is <see langword="false"/>.
    /// <see langword="null"/> when succeeded.
    /// </summary>
    [DataMember(Name = "failureReason")]
    public FailureReason? FailureReason { get; init; }

    /// <summary>
    /// The element's value before the strategy was applied, if captured.
    /// Used by the act-tool layer to compute <c>stateChanged</c> in the StateDeltaDto.
    /// </summary>
    [DataMember(Name = "previousValue")]
    public string? PreviousValue { get; init; }

    /// <summary>The input tier the strategy executed at.</summary>
    [DataMember(Name = "chosenTier")]
    public InputTier ChosenTier { get; init; }
}
