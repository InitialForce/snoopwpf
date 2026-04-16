namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Event arguments carried by <see cref="IIdlingResource.IdleChanged"/>.
/// PRD §8.2 / FD-4.
/// </summary>
public sealed class IdleChangedEventArgs : EventArgs
{
    /// <summary><see langword="true"/> when the resource just became idle.</summary>
    public required bool IsIdle { get; init; }

    /// <summary>UTC timestamp at which the transition occurred.</summary>
    public required DateTimeOffset At { get; init; }
}
