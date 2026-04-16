namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Thread-safe collection of <see cref="IIdlingResource"/> instances.
/// Evaluates an AND-gate: the registry is idle only when every registered
/// resource is idle.  Raises <see cref="IdleChanged"/> whenever the
/// aggregate idle state transitions.
/// </summary>
public sealed class IdlingResourceRegistry : IDisposable
{
    private readonly object gate = new object();

    private readonly List<IIdlingResource> resources = new List<IIdlingResource>();

    private int isIdle = 1; // 0 = busy, 1 = idle  — empty registry starts idle

    private bool disposed;

    /// <summary>
    /// Raised whenever the aggregate idle state changes.
    /// May be raised on any thread.
    /// </summary>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    /// <summary>
    /// <see langword="true"/> when every registered resource reports idle
    /// (empty registry is considered idle).
    /// </summary>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    // ------------------------------------------------------------------ //
    //  Registration
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Registers a resource and begins observing its <see cref="IIdlingResource.IdleChanged"/> event.
    /// </summary>
    public void Register(IIdlingResource resource)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(IdlingResourceRegistry));
        }

        // Subscribe before inserting so no IdleChanged transition can be missed between
        // unlock and subscribe (FX-M4 TOCTOU fix).
        resource.IdleChanged += this.OnResourceIdleChanged;

        lock (this.gate)
        {
            this.resources.Add(resource);
        }

        this.Revaluate();
    }

    /// <summary>
    /// Unregisters a resource and stops observing its events.
    /// </summary>
    public void Unregister(IIdlingResource resource)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        resource.IdleChanged -= this.OnResourceIdleChanged;

        lock (this.gate)
        {
            this.resources.Remove(resource);
        }

        this.Revaluate();
    }

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void OnResourceIdleChanged(object? sender, IdleChangedEventArgs e)
    {
        this.Revaluate();
    }

    private void Revaluate()
    {
        bool nowIdle;

        lock (this.gate)
        {
            nowIdle = this.resources.Count == 0 || this.resources.All(r => r.IsIdle);
        }

        int newValue = nowIdle ? 1 : 0;

        // Only raise the event when the aggregate state actually changes.
        int previous = Interlocked.Exchange(ref this.isIdle, newValue);
        if (previous != newValue)
        {
            this.IdleChanged?.Invoke(this, new IdleChangedEventArgs
            {
                IsIdle = nowIdle,
                At = DateTimeOffset.UtcNow,
            });
        }
    }

    // ------------------------------------------------------------------ //
    //  IDisposable
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        List<IIdlingResource> snapshot;

        lock (this.gate)
        {
            snapshot = new List<IIdlingResource>(this.resources);
            this.resources.Clear();
        }

        foreach (var r in snapshot)
        {
            r.IdleChanged -= this.OnResourceIdleChanged;
        }
    }
}
