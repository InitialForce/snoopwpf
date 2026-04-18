namespace SnoopWPF.Agent.Server;

using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents a running Mode 2 (warm-attach) brokered pipe server session.
/// Dispose to stop listening and release the named pipe.
/// </summary>
/// <remarks>
/// Returned by <see cref="SnoopAgent.StartBrokeredServerAsync"/>.  The caller
/// (Application.Startup or equivalent) should read <see cref="PipeName"/> and
/// <see cref="SessionToken"/> immediately after the task completes and pass them
/// to the manifest writer (bd-1a9.24).  The pipe server continues accepting exactly
/// one broker connection until the handle is disposed or the cancellation token fires.
/// </remarks>
public sealed class BrokeredServerHandle : IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource cts;
    private readonly Task listenerTask;
    private readonly ManifestHandle? manifestHandle;
    private int disposedFlag;

    internal BrokeredServerHandle(
        string pipeName,
        string sessionToken,
        CancellationTokenSource cts,
        Task listenerTask,
        ManifestHandle? manifestHandle = null)
    {
        this.PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        this.SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        this.listenerTask = listenerTask ?? throw new ArgumentNullException(nameof(listenerTask));
        this.manifestHandle = manifestHandle;
    }

    /// <summary>
    /// The name of the named pipe that the broker must connect to.
    /// Pass this to the manifest writer (bd-1a9.24) so the broker can discover the pipe.
    /// Set to <see langword="null"/> when the handle has been disposed.
    /// </summary>
    public string? PipeName { get; private set; }

    /// <summary>
    /// The 64-character hex session token (256-bit HMAC key) used for the pipe handshake.
    /// Pass this to the manifest writer (bd-1a9.24) alongside <see cref="PipeName"/>.
    /// Treat this value like a password — do not log or write to stdout.
    /// Set to <see langword="null"/> when the handle has been disposed.
    /// </summary>
    public string? SessionToken { get; private set; }

    /// <summary>
    /// <see langword="true"/> once a broker has successfully completed the HMAC handshake
    /// and is actively connected to the pipe.
    /// </summary>
    public bool IsConnected { get; internal set; }

    /// <summary>
    /// Stops the pipe server listener, cancels any pending
    /// <see cref="NamedPipeServerStream.WaitForConnectionAsync"/> call, and releases
    /// the underlying named pipe handle.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposedFlag, 1) == 1)
        {
            return;
        }

        this.cts.Cancel();

        // Wait briefly for the listener task to observe the cancellation.
        // We do not throw from Dispose; if the task does not finish in time it will
        // be collected when the process exits.
        try
        {
            this.listenerTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // OperationCanceledException or IOException from pipe close — expected.
        }

        this.cts.Dispose();

        // Best-effort manifest cleanup; errors are swallowed inside ManifestHandle.Dispose().
        this.manifestHandle?.Dispose();

        // Null security-sensitive references so GC can collect them sooner.
        this.PipeName = null;
        this.SessionToken = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposedFlag, 1) == 1)
        {
            return;
        }

        this.cts.Cancel();

        try
        {
            await this.listenerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected — cancellation triggered by Dispose.
        }
        catch (Exception)
        {
            // Any other exception from the listener is swallowed; Dispose must not throw.
        }

        this.cts.Dispose();

        // Best-effort manifest cleanup; errors are swallowed inside ManifestHandle.Dispose().
        this.manifestHandle?.Dispose();

        this.PipeName = null;
        this.SessionToken = null;
    }
}
