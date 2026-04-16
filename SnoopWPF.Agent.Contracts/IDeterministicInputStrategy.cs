namespace SnoopWPF.Agent.Contracts;

using System.Threading;
using System.Windows; // DependencyObject (WPF)
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// A single deterministic input strategy that knows how to perform one class of UI action
/// at a specific <see cref="InputTier"/>.
/// </summary>
/// <remarks>
/// Per FD-5 (PRD §4.4). All implementations must be registered with and selected via
/// <c>InputStrategySelector</c>. No tool handler may invoke a strategy directly (S2).
/// Strategy-layer gating (tier, Automation, Mutation) is enforced by the selector,
/// not by individual strategy implementations.
/// </remarks>
public interface IDeterministicInputStrategy
{
    /// <summary>The input tier at which this strategy operates.</summary>
    InputTier Tier { get; }

    /// <summary>
    /// Returns <see langword="true"/> if this strategy can handle the given
    /// <paramref name="intent"/> on <paramref name="target"/>.
    /// </summary>
    /// <param name="intent">The requested action.</param>
    /// <param name="target">The WPF element to operate on.</param>
    bool CanHandle(InputIntent intent, DependencyObject target);

    /// <summary>
    /// Executes the strategy against <paramref name="target"/> for the given
    /// <paramref name="intent"/>.
    /// </summary>
    /// <param name="target">The WPF element to operate on.</param>
    /// <param name="intent">The requested action, including arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeterministicInputResult"/> describing the outcome.</returns>
    DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct);
}
