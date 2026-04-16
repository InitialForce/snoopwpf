namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Threading;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// An <see cref="IIdlingResource"/> that tracks the running state of a single
/// <see cref="Storyboard"/>.
///
/// Becomes non-idle when the storyboard starts (or resumes) and idle again
/// once it completes, pauses, stops, or is removed.
/// </summary>
public sealed class StoryboardResource : IIdlingResource, IDisposable
{
    private readonly Storyboard storyboard;

    private int isIdle; // 0 = busy, 1 = idle

    private bool disposed;

    /// <summary>
    /// Initialises a new instance and hooks the storyboard's state events.
    /// </summary>
    /// <param name="storyboard">The storyboard to observe.</param>
    /// <param name="initiallyRunning">
    ///   Pass <see langword="true"/> if the storyboard is already running
    ///   at construction time.
    /// </param>
    public StoryboardResource(Storyboard storyboard, bool initiallyRunning = false)
    {
        this.storyboard = storyboard ?? throw new ArgumentNullException(nameof(storyboard));
        this.isIdle = initiallyRunning ? 0 : 1;

        this.storyboard.CurrentStateInvalidated += this.OnCurrentStateInvalidated;
        this.storyboard.Completed += this.OnCompleted;
    }

    // ------------------------------------------------------------------ //
    //  IIdlingResource
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => $"Storyboard({this.storyboard.Name ?? "(anonymous)"})";

    /// <inheritdoc/>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    /// <inheritdoc/>
    public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

    /// <inheritdoc/>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void OnCurrentStateInvalidated(object? sender, EventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        // sender is the Clock driving the storyboard.
        var clock = sender as Clock;
        bool isActive = clock?.CurrentState == ClockState.Active;
        this.SetIdle(!isActive);
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        this.SetIdle(true);
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
        this.storyboard.CurrentStateInvalidated -= this.OnCurrentStateInvalidated;
        this.storyboard.Completed -= this.OnCompleted;
    }
}
