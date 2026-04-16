namespace SnoopWPF.Agent.Contracts;

using System;
using System.Windows.Threading;

/// <summary>
/// Abstraction for a single "idle gate" that can report whether a particular
/// WPF subsystem (Dispatcher queue, composition rendering, animation, timer)
/// is currently quiescent.
///
/// PRD §8.2 / FD-4 — frozen signature.
/// </summary>
public interface IIdlingResource
{
    /// <summary>Human-readable name used in diagnostics and logging.</summary>
    string Name { get; }

    /// <summary>
    /// <see langword="true"/> when the subsystem is currently idle.
    /// Must be thread-safe to read.
    /// </summary>
    bool IsIdle { get; }

    /// <summary>
    /// The <see cref="DispatcherPriority"/> at which this resource checks its
    /// idle condition.  Must be ≥ <see cref="DispatcherPriority.ContextIdle"/>
    /// per PRD §8.2 W3-H1.
    /// </summary>
    DispatcherPriority CheckPriority { get; }

    /// <summary>Raised (on any thread) whenever <see cref="IsIdle"/> changes.</summary>
    event EventHandler<IdleChangedEventArgs>? IdleChanged;
}
