namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;
using SnoopWPF.Agent.Engine.StateDelta;
using SnoopWPF.Agent.Engine.Sync;

/// <summary>
/// Core inspection engine. Implements <see cref="ISnoopInspector"/> by marshaling all WPF operations
/// to the provided Dispatcher. Concurrency is capped at 3 simultaneous Dispatcher invocations.
/// </summary>
public sealed partial class SnoopInspector : ISnoopInspector, IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly object? rootTarget;
    private readonly SnoopInspectorOptions options;
    private readonly SessionPolicy? sessionPolicy;

    private readonly NodeRegistry nodeRegistry;
    private readonly CursorManager cursorManager;
    private readonly LocatorResolver locatorResolver;
    private readonly Binding.BindingResolver bindingResolver = new();

    // M2-10: last-poll snapshot for incremental "removed" detection.
    // Stores the liveNodeIds captured at the most-recently returned TreeVersion.
    // Access only on the Dispatcher thread (set inside RunOnDispatcherAsync).
    private long lastPollVersion = -1;
    private System.Collections.Generic.HashSet<string>? lastPollLiveIds;

    // Max 3 concurrent Dispatcher operations.
    private readonly SemaphoreSlim concurrencySemaphore = new(3, 3);

    // FX-C1: CTS cancelled in Dispose() to unblock concurrent WaitAsync calls gracefully.
    private readonly CancellationTokenSource disposeCts = new();

    // M2-11: nested-pump guard — 0 = idle, 1 = pump in progress.
    // Instance-level so it is safe across thread-pool continuation migrations.
    private int pumpInProgress;

    // Use int so Interlocked.Exchange can guarantee atomicity (volatile bool has no atomic swap).
    private int disposed;

    /// <summary>
    /// Initializes a new SnoopInspector.
    /// </summary>
    /// <param name="dispatcher">The WPF Dispatcher for the target application.</param>
    /// <param name="rootTarget">
    /// The root inspection target — typically <c>Application.Current</c> in NuGet mode,
    /// or a specific injection root in injection mode.
    /// </param>
    /// <param name="options">Optional configuration; defaults are applied if null.</param>
    /// <param name="sessionPolicy">
    /// Optional session policy. When provided, MF-11 is enforced: if
    /// <see cref="SessionPolicy.Mode"/> is <see cref="SessionMode.Injection"/> and
    /// <see cref="SessionPolicy.EnableRedaction"/> is <see langword="false"/>, an
    /// <see cref="InvalidOperationException"/> is thrown immediately.
    /// </param>
    public SnoopInspector(
        Dispatcher dispatcher,
        object? rootTarget = null,
        SnoopInspectorOptions? options = null,
        SessionPolicy? sessionPolicy = null)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.rootTarget = rootTarget;
        this.options = options ?? new SnoopInspectorOptions();
        this.sessionPolicy = sessionPolicy;

        if (sessionPolicy is not null)
        {
            EnforceInjectionRedaction(sessionPolicy);
        }

        this.nodeRegistry = new NodeRegistry();

        // FX6-A2: generate a per-session random signing key for cursor node-binding.
        // Each SnoopInspector instance gets its own 32-byte key so that cursors cannot
        // be replayed across sessions or against different node contexts.
#if NET6_0_OR_GREATER
        var cursorSigningKey = RandomNumberGenerator.GetBytes(32);
#else
        var cursorSigningKey = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(cursorSigningKey);
        }
#endif
        this.cursorManager = new CursorManager(signingKey: cursorSigningKey);
        this.locatorResolver = new LocatorResolver(this.nodeRegistry);
    }

    /// <summary>
    /// Belt-and-braces MF-11 assertion: Injection mode must always have redaction enabled.
    /// Throws <see cref="InvalidOperationException"/> if a bypass <see cref="SessionPolicy"/>
    /// is supplied (i.e., constructed without going through <see cref="SessionPolicy.Create"/>).
    /// </summary>
    /// <param name="policy">The session policy to validate.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="policy"/> has <c>Mode == Injection</c> and
    /// <c>EnableRedaction == false</c>.
    /// </exception>
    public static void EnforceInjectionRedaction(SessionPolicy policy)
    {
        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        if (policy.Mode == SessionMode.Injection && !policy.EnableRedaction)
        {
            throw new InvalidOperationException(
                "Injection mode requires EnableRedaction=true (MF-11). Policy was constructed bypassing SessionPolicy.Create.");
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        // Interlocked.Exchange ensures at most one caller performs disposal (double-dispose safe).
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.disposeCts.Cancel();
        this.disposeCts.Dispose();
        this.nodeRegistry.Dispose();
        this.cursorManager.Dispose();
        this.concurrencySemaphore.Dispose();
    }

    // -------------------------------------------------------------------------
    // ISnoopInspector — implemented methods
    // -------------------------------------------------------------------------

    /// <inheritdoc/>

    /// <summary>
    /// Resolves a <see cref="WpfLocator"/> to a stable node ID by delegating to
    /// <see cref="LocatorResolver"/> on the WPF Dispatcher thread.
    /// Throws <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/>
    /// if the 100-entry NodeRegistry growth cap is exceeded.
    /// </summary>
    private Task<string> ResolveLocatorAsync(WpfLocator locator, CancellationToken ct)
    {
        if (locator is null)
        {
            throw new ArgumentNullException(nameof(locator));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            var root = this.GetEffectiveRootTarget();
            return this.locatorResolver.Resolve(locator, root);
        }, ct);
    }

    // -------------------------------------------------------------------------
    // Threading helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs a synchronous action on the WPF Dispatcher with concurrency limiting and timeout.
    /// Two-phase timeout:
    ///   Phase 1 (DispatcherAcceptanceTimeoutMs): Dispatcher acceptance — DispatcherBusy if exceeded.
    ///   Phase 2 (TimeoutMs): Execution — OperationTimedOut if exceeded.
    /// </summary>
    private async Task<T> RunOnDispatcherAsync<T>(Func<T> action, CancellationToken ct)
    {
        this.ThrowIfDisposed();

        // Guard: must not be called from the Dispatcher thread (would deadlock).
        Debug.Assert(
            !this.dispatcher.CheckAccess(),
            "SnoopInspector methods must not be called from the Dispatcher thread.");

        // Guard: Dispatcher must not be shutting down.
        if (this.dispatcher.HasShutdownStarted)
        {
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "The WPF Dispatcher has shut down.",
                suggestions: new[] { SnoopSuggestions.SessionNotFound });
        }

        // FX-C1: link caller CT with dispose CTS so WaitAsync throws OperationCanceledException
        // (not ObjectDisposedException) if Dispose() races with this call.
        // TOCTOU guard: Dispose() may fire between ThrowIfDisposed and here, causing
        // CreateLinkedTokenSource to throw ODE. Translate to OCE — callers tolerate cancellation.
        CancellationTokenSource semWaitCts;
        try
        {
            semWaitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, this.disposeCts.Token);
        }
        catch (ObjectDisposedException)
        {
            throw new OperationCanceledException("SnoopInspector was disposed.", ct);
        }

        using var semWaitCtsDispose = semWaitCts;
        await this.concurrencySemaphore.WaitAsync(semWaitCts.Token).ConfigureAwait(false);

        try
        {
            // Queue work on the Dispatcher at Send priority.
            var dispatcherOp = this.dispatcher.InvokeAsync(action, DispatcherPriority.Send, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(this.options.TimeoutMs);

            var dispatcherTask = dispatcherOp.Task;

            // Track total budget elapsed so Phase 2 only waits for the *remaining* time.
            var sw = Stopwatch.StartNew();

            // Phase 1: Did the Dispatcher accept (start) the work within the acceptance window?
            var acceptanceDeadline = Task.Delay(this.options.DispatcherAcceptanceTimeoutMs, timeoutCts.Token);
            var firstToFinish = await Task.WhenAny(dispatcherTask, acceptanceDeadline).ConfigureAwait(false);

            if (firstToFinish == acceptanceDeadline && !dispatcherTask.IsCompleted)
            {
                dispatcherOp.Abort();
                ct.ThrowIfCancellationRequested();

                throw new SnoopException(
                    SnoopErrorCode.DispatcherBusy,
                    "The WPF Dispatcher did not accept work within the acceptance timeout.",
                    suggestions: new[] { SnoopSuggestions.DispatcherBusy });
            }

            // Phase 2: Work was accepted. Wait for completion within the *remaining* budget.
            var remainingMs = Math.Max(0, this.options.TimeoutMs - (int)sw.ElapsedMilliseconds);
            var completionDeadline = Task.Delay(remainingMs, timeoutCts.Token);
            var finalResult = await Task.WhenAny(dispatcherTask, completionDeadline).ConfigureAwait(false);

            if (finalResult == completionDeadline && !dispatcherTask.IsCompleted)
            {
                dispatcherOp.Abort();
                ct.ThrowIfCancellationRequested();

                throw new SnoopException(
                    SnoopErrorCode.OperationTimedOut,
                    $"The Dispatcher operation did not complete within {this.options.TimeoutMs}ms.",
                    suggestions: new[] { SnoopSuggestions.OperationTimedOut });
            }

            // Propagate exceptions from the Dispatcher lambda.
            return await dispatcherTask.ConfigureAwait(false);
        }
        finally
        {
            // FX2-C1: Dispose() may have run on another thread between acquiring and
            // releasing the slot. SemaphoreSlim.Release on a disposed instance throws
            // ObjectDisposedException → would surface as an unobserved task exception
            // (AppDomain-fatal on net462). Swallowing ODE is the correct behaviour here
            // because the disposal path has already released all backing resources.
            try
            {
                this.concurrencySemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Inspector was disposed while this call was in flight; nothing to release.
            }
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this.disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SnoopInspector));
        }
    }

    /// <summary>
    /// FX-M8: Returns a TierMismatch failure result if the session policy's MaxTier is below
    /// <see cref="InputTier.L1"/> (mutation tier). Call at the top of every mutation method.
    /// Returns <see langword="null"/> when the tier check passes.
    /// </summary>
    private StateDeltaDto? EnsureMutationTier()
    {
        if (this.sessionPolicy is not null && this.sessionPolicy.MaxTier < InputTier.L1)
        {
            return new StateDeltaDto
            {
                Success = false,
                ElementVisible = false,
                StateChanged = false,
                FailureReason = FailureReason.TierMismatch,
                Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.TierMismatch, null),
            };
        }

        return null;
    }

    private object GetEffectiveRootTarget()
    {
        if (this.rootTarget is not null)
        {
            return this.rootTarget;
        }

        var app = Application.Current;
        if (app is null)
        {
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "Application.Current is null — application may not be initialized.",
                suggestions: new[] { SnoopSuggestions.SessionNotFound });
        }

        return app;
    }

    private object ResolveRootTarget(string? rootNodeId)
    {
        if (rootNodeId is null)
        {
            return this.GetEffectiveRootTarget();
        }

        return this.ResolveNodeOrThrow(rootNodeId);
    }

    private object ResolveNodeOrThrow(string nodeId)
    {
        // Support path alias: "Window\Grid\Button"
        if (nodeId.IndexOf("\\", StringComparison.Ordinal) >= 0)
        {
            return this.ResolvePathAlias(nodeId);
        }

        var obj = this.nodeRegistry.TryResolve(nodeId);
        if (obj is null)
        {
            throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Node '{nodeId}' was not found in the registry — it may have been garbage collected.",
                targetId: nodeId,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
        }

        return obj;
    }

    private object ResolvePathAlias(string path)
    {
        // Walk the tree from root, matching type names for each segment.
        var segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Empty path alias: '{path}'.",
                targetId: path,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
        }

        using var treeService = TreeService.From(TreeType.Visual);
        var root = treeService.Construct(this.GetEffectiveRootTarget(), parent: null);

        return FindByPathSegments(root, segments, 0)
            ?? throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Path alias '{path}' did not match any element in the tree.",
                targetId: path,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
    }

    private static object? FindByPathSegments(TreeItem item, string[] segments, int segmentIndex)
    {
        if (segmentIndex >= segments.Length)
        {
            return item.Target;
        }

        var segment = segments[segmentIndex];

        if (!string.Equals(item.TargetType?.Name, segment, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (segmentIndex == segments.Length - 1)
        {
            return item.Target;
        }

        foreach (var child in item.Children)
        {
            var found = FindByPathSegments(child, segments, segmentIndex + 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void VerifyElementConnectivity(object target, string nodeId)
    {
        if (target is Window window)
        {
            // For Windows, use IsInitialized — PresentationSource can return null for hidden windows.
            if (!window.IsInitialized)
            {
                throw new SnoopException(
                    SnoopErrorCode.NodeNotFound,
                    $"Window node '{nodeId}' is no longer initialized.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.NodeNotFound });
            }
        }

        // Non-Visual and Visual nodes without a presentation source are allowed through
        // (popups, logical-only nodes, etc.). Connectivity failures surface as exceptions
        // during property reads rather than as an explicit gate here.
    }

    private static TreeType ParseTreeType(string treeType)
    {
        if (string.Equals(treeType, "Visual", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Visual;
        }

        if (string.Equals(treeType, "Logical", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Logical;
        }

        if (string.Equals(treeType, "Automation", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Automation;
        }

        // Default to Visual if unrecognised.
        return TreeType.Visual;
    }

    private NodeDto BuildNodeDtoRecursive(
        TreeItem item,
        int currentDepth,
        int maxDepth,
        List<string>? includeProperties,
        ref int nodeCount,
        int maxNodes,
        ref bool truncated)
    {
        nodeCount++;

        List<NameValuePairDto>? props = null;

        if (includeProperties is { Count: > 0 } && item.Target is DependencyObject depObj)
        {
            props = this.ReadNamedProperties(depObj, includeProperties);
        }

        var dto = DtoProjection.ToNodeDto(item, this.nodeRegistry, currentDepth);
        dto.Properties = props;

        if (currentDepth >= maxDepth || nodeCount >= maxNodes)
        {
            if (item.Children.Count > 0)
            {
                dto.ChildrenTruncated = true;
                truncated = true;
            }

            return dto;
        }

        if (item.Children.Count > 0)
        {
            dto.Children = new List<NodeDto>(item.Children.Count);

            foreach (var child in item.Children)
            {
                if (nodeCount >= maxNodes)
                {
                    dto.ChildrenTruncated = true;
                    truncated = true;
                    break;
                }

                dto.Children.Add(this.BuildNodeDtoRecursive(
                    child, currentDepth + 1, maxDepth, includeProperties,
                    ref nodeCount, maxNodes, ref truncated));
            }
        }

        return dto;
    }

    private List<NameValuePairDto> ReadNamedProperties(DependencyObject target, List<string> propertyNames)
    {
        var result = new List<NameValuePairDto>(propertyNames.Count);

        var props = PropertyInformation.GetProperties(target);
        try
        {
            foreach (var propName in propertyNames)
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propName, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    var propType = (Type?)match.PropertyType;
                    var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propName, propType);
                    var value = isRedacted ? "[REDACTED]" : (match.StringValue ?? string.Empty);

                    result.Add(new NameValuePairDto { Name = propName, Value = value });
                }
            }
        }
        finally
        {
            foreach (var prop in props)
            {
                prop.Teardown();
                StopChangeTimer(prop);
            }
        }

        return result;
    }

    /// <summary>
    /// Stops the orphaned DispatcherTimer that PropertyInformation.Teardown() does not stop.
    /// Uses reflection to access the private changeTimer field.
    /// </summary>
    private static void StopChangeTimer(PropertyInformation prop)
    {
        try
        {
            var field = typeof(PropertyInformation).GetField(
                "changeTimer",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field?.GetValue(prop) is DispatcherTimer timer)
            {
                timer.Stop();
            }
        }
        catch
        {
            // Best-effort. If reflection fails, the timer will expire naturally.
        }
    }

    // -------------------------------------------------------------------------
    // Private nested types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Avoids ValueTuple syntax (not available in net462 without a NuGet polyfill)
    /// when returning two values from a Dispatcher-marshalled lambda inside
    /// <see cref="WaitForPropertyAsync"/>.
    /// </summary>
    private readonly struct WaitForPropertyPollResult
    {
        public WaitForPropertyPollResult(bool found, string? value)
        {
            this.Found = found;
            this.Value = value;
        }

        public bool Found { get; }

        public string? Value { get; }
    }
}
