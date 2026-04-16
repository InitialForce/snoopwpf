namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Threading;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// An <see cref="IIdlingResource"/> that tracks the WPF <see cref="Dispatcher"/>
/// message queue.  Becomes idle when no higher-priority work is pending
/// (evaluated at <see cref="DispatcherPriority.ContextIdle"/>).
/// </summary>
public sealed class DispatcherIdlingResource : IIdlingResource, IDisposable
{
    private readonly Dispatcher dispatcher;

    private DispatcherOperation? pendingProbe;

    private int isIdle; // 0 = busy, 1 = idle

    private bool disposed;

    /// <summary>
    /// Initialises a new instance and immediately schedules the first
    /// ContextIdle probe on <paramref name="dispatcher"/>.
    /// </summary>
    public DispatcherIdlingResource(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        // Assume non-idle until we get our first ContextIdle probe through.
        this.isIdle = 0;
        this.ScheduleProbe();
    }

    // ------------------------------------------------------------------ //
    //  IIdlingResource
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => "Dispatcher";

    /// <inheritdoc/>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    /// <inheritdoc/>
    public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

    /// <inheritdoc/>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void ScheduleProbe()
    {
        if (this.disposed)
        {
            return;
        }

        this.pendingProbe = this.dispatcher.InvokeAsync(this.OnContextIdle, DispatcherPriority.ContextIdle);
    }

    private void OnContextIdle()
    {
        if (this.disposed)
        {
            return;
        }

        this.SetIdle(true);
        // Re-arm: queue another probe so we detect future busy periods.
        this.ScheduleProbe();
    }

    /// <summary>
    /// Call this when higher-priority work is dispatched so the resource
    /// transitions back to non-idle immediately.
    /// </summary>
    internal void SignalBusy()
    {
        this.SetIdle(false);
    }

    private void SetIdle(bool idle)
    {
        int newValue = idle ? 1 : 0;
        int previous = Interlocked.Exchange(ref this.isIdle, newValue);
        if (previous != newValue)
        {
            this.IdleChanged?.Invoke(this, new IdleChangedEventArgs
            {
                IsIdle = idle,
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
        this.pendingProbe?.Abort();
        this.pendingProbe = null;
    }
}
