namespace SnoopWPF.Agent.Engine.Sync;

using System;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// An <see cref="IIdlingResource"/> that tracks WPF composition rendering
/// activity via <see cref="CompositionTarget.Rendering"/>.
///
/// Becomes non-idle when a rendering frame fires; becomes idle again once
/// no further frames have fired within a short quiescence window checked at
/// <see cref="DispatcherPriority.ContextIdle"/>.
/// </summary>
public sealed class CompositionRenderingResource : IIdlingResource, IDisposable
{
    private readonly Dispatcher dispatcher;

    private DispatcherOperation? idleProbe;

    private int isIdle; // 0 = busy, 1 = idle

    private bool disposed;

    private bool subscribed;

    /// <summary>
    /// Initialises a new instance.  Subscribes to <see cref="CompositionTarget.Rendering"/>
    /// on the dispatcher thread.
    /// </summary>
    public CompositionRenderingResource(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        // Start idle; we subscribe to rendering on the dispatcher thread.
        this.isIdle = 1;
        this.dispatcher.InvokeAsync(this.Subscribe, DispatcherPriority.Send);
    }

    // ------------------------------------------------------------------ //
    //  IIdlingResource
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    public string Name => "CompositionRendering";

    /// <inheritdoc/>
    public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

    /// <inheritdoc/>
    public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

    /// <inheritdoc/>
    public event EventHandler<IdleChangedEventArgs>? IdleChanged;

    // ------------------------------------------------------------------ //
    //  Internal
    // ------------------------------------------------------------------ //

    private void Subscribe()
    {
        if (this.disposed || this.subscribed)
        {
            return;
        }

        this.subscribed = true;
        CompositionTarget.Rendering += this.OnRendering;
    }

    private void Unsubscribe()
    {
        if (!this.subscribed)
        {
            return;
        }

        this.subscribed = false;
        CompositionTarget.Rendering -= this.OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        // A frame is being rendered — mark busy.
        this.SetIdle(false);

        // Cancel any existing idle probe and schedule a fresh one.
        this.idleProbe?.Abort();
        this.idleProbe = this.dispatcher.InvokeAsync(this.OnContextIdle, DispatcherPriority.ContextIdle);
    }

    private void OnContextIdle()
    {
        if (this.disposed)
        {
            return;
        }

        // If no new rendering event fired before the ContextIdle probe ran,
        // the composition system is quiescent.
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
        this.idleProbe?.Abort();
        this.idleProbe = null;
        this.dispatcher.InvokeAsync(this.Unsubscribe, DispatcherPriority.Send);
    }
}
