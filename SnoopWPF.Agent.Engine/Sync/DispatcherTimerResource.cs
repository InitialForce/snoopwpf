namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Threading;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// An <see cref="IIdlingResource"/> that tracks whether a
/// <see cref="DispatcherTimer"/> is currently enabled (ticking).
///
/// Idle = timer is stopped.  Non-idle = timer is running.
/// </summary>
public sealed class DispatcherTimerResource : IIdlingResource, IDisposable
{
    private readonly DispatcherTimer timer;

    private int isIdle; // 0 = busy, 1 = idle

    private bool disposed;

    /// <summary>
    /// Initialises a new instance.  Reflects <paramref name="timer"/>'s
    /// current <see cref="DispatcherTimer.IsEnabled"/> state immediately.
    /// </summary>
    public DispatcherTimerResource(DispatcherTimer timer)
    {
        this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
        // Reflect current state immediately.
        this.isIdle = timer.IsEnabled ? 0 : 1;

        this.timer.Tick += this.OnTick;
    }

    // ------------------------------------------------------------------ //
    //  IIdlingResource
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => "DispatcherTimer";

    /// <inheritdoc/>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    /// <inheritdoc/>
    public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

    /// <inheritdoc/>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    // ------------------------------------------------------------------ //
    //  Public helpers
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Synchronise the idle state to the timer's current <see cref="DispatcherTimer.IsEnabled"/>.
    /// Call this after starting or stopping the timer from outside if you need
    /// immediate notification (the <see cref="DispatcherTimer.Tick"/> handler
    /// covers the running case automatically).
    /// </summary>
    public void Refresh()
    {
        this.SetIdle(!this.timer.IsEnabled);
    }

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void OnTick(object? sender, EventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        // Each tick proves the timer is still running — non-idle.
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
        this.timer.Tick -= this.OnTick;
    }
}
