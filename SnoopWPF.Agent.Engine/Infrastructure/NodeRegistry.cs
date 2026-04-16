namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Thread-safe bidirectional mapping between WPF objects and stable string node IDs.
/// Uses weak references so that the registry does not prevent garbage collection of WPF objects.
/// </summary>
public sealed class NodeRegistry : IDisposable
{
    // Forward: object → NodeRegistration (weak — ConditionalWeakTable holds weak ref to key)
    private readonly ConditionalWeakTable<object, NodeRegistration> forward = new();

    // Reverse: nodeId string → weak ref to object
    private readonly ConcurrentDictionary<string, WeakReference<object>> reverse = new();

    private int counter;

    private readonly Timer sweepTimer;

    private bool disposed;

    /// <summary>
    /// Creates a NodeRegistry with a configurable sweep interval (default 60s).
    /// </summary>
    /// <param name="sweepInterval">How often to sweep for dead entries. Pass a short interval in tests.</param>
    public NodeRegistry(TimeSpan sweepInterval = default)
    {
        if (sweepInterval == default)
        {
            sweepInterval = TimeSpan.FromSeconds(60);
        }

        this.sweepTimer = new Timer(_ => this.ForceSweep(), null, sweepInterval, sweepInterval);
    }

    private static void ThrowIfDisposed(bool disposed, object instance)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(instance.GetType().Name);
        }
    }

    /// <summary>
    /// Gets or creates a stable node ID for the given WPF object.
    /// Idempotent: returns the same ID for the same object reference.
    /// </summary>
    public string GetOrCreateId(object element)
    {
        ThrowIfDisposed(this.disposed, this);

        // Fast path: already registered
        if (this.forward.TryGetValue(element, out var existing))
        {
            return existing.Id;
        }

        // Slow path: create new registration
        var newCounter = Interlocked.Increment(ref this.counter);
        var id = $"0:{newCounter}";
        var registration = new NodeRegistration(id, element);

        // GetOrAdd is not available on ConditionalWeakTable, handle race via try/catch
        NodeRegistration actual;
        try
        {
            this.forward.Add(element, registration);
            actual = registration;
        }
        catch (ArgumentException)
        {
            // Another thread registered this object first
            if (this.forward.TryGetValue(element, out var raced))
            {
                actual = raced;
            }
            else
            {
                actual = registration;
            }
        }

        // Register in reverse map. Use TryAdd — if already present (race), that's fine.
        this.reverse.TryAdd(actual.Id, new WeakReference<object>(element));

        // Size cap: trigger sweep if reverse dict is getting large
        if (this.reverse.Count >= 10_000)
        {
            this.ForceSweep();
        }

        return actual.Id;
    }

    /// <summary>
    /// Resolves a node ID back to the live WPF object.
    /// Returns null if the object has been garbage collected or the ID is unknown.
    /// </summary>
    public object? TryResolve(string id)
    {
        ThrowIfDisposed(this.disposed, this);

        if (!this.reverse.TryGetValue(id, out var weakRef))
        {
            return null;
        }

        if (weakRef.TryGetTarget(out var target))
        {
            return target;
        }

        // Object was collected; lazy-prune the reverse entry
        this.reverse.TryRemove(id, out _);
        return null;
    }

    /// <summary>
    /// Clears all registrations. Used for session cleanup.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed(this.disposed, this);

        this.reverse.Clear();
        this.ClearForwardTable();
        this.counter = 0;
    }

    private void ClearForwardTable()
    {
        // ConditionalWeakTable.Clear() exists on .NET Core but not .NET 4.6.2.
        // On .NET 4.6.2 entries expire naturally when objects are collected.
#if NET6_0_OR_GREATER
        this.forward.Clear();
#endif
    }

    /// <summary>
    /// Sweeps the reverse dictionary for dead weak references.
    /// Called automatically every sweepInterval; also exposed for unit tests.
    /// </summary>
    internal void ForceSweep()
    {
        foreach (var pair in this.reverse)
        {
            if (!pair.Value.TryGetTarget(out _))
            {
                this.reverse.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.sweepTimer.Dispose();
        this.reverse.Clear();
    }

    /// <summary>
    /// Internal registration record stored in the forward ConditionalWeakTable.
    /// </summary>
    internal sealed class NodeRegistration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeRegistration"/> class.
        /// </summary>
        public NodeRegistration(string id, object element)
        {
            this.Id = id;
            this.ParentRef = new WeakReference<object>(element);
        }

        public string Id { get; }

        public WeakReference<object> ParentRef { get; set; }
    }
}
