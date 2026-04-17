// SnoopInspector.Wait.cs
// FX6-A7: Async waiting and polling methods.

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

/// <content/>
public sealed partial class SnoopInspector
{

    // ── M2-09: wpf_wait_for_property ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Contracts.Dtos.WaitForPropertyResultDto> WaitForPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string? expectedValue,
        int timeoutMs,
        string presenceExpected,
        CancellationToken ct)
    {
        if (locator is null)
        {
            throw new ArgumentNullException(nameof(locator));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("propertyName must not be null or empty.", nameof(propertyName));
        }

        var absent = string.Equals(presenceExpected, "absent", StringComparison.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(timeoutMs);
        var pollCount = 0;
        const int MinPollIntervalMs = 50;

        // FX6-A3: resolve the locator to a weak reference ONCE before the poll loop.
        // Re-resolving on every poll would call LocatorResolver.Resolve which calls
        // NodeRegistry.GetOrCreateId on each traversed node, creating up to 300 registry
        // entries per poll × 300 polls = 90 000 entries per 30-second wait — OOM risk.
        // Instead, resolve once on the first poll, obtain the WeakReference<object> from
        // NodeRegistry, then poll the weak reference directly on subsequent iterations.
        WeakReference<object>? elementWeakRef = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            pollCount++;

            string? actualValue = null;
            bool elementFound = false;
            bool dispatcherBusy = false;

            try
            {
                var pollResult = await this.RunOnDispatcherAsync(() =>
                {
                    // FX6-A3: on first poll, resolve locator → nodeId → WeakReference.
                    // On subsequent polls, use the cached weak reference directly.
                    object? resolved;
                    if (elementWeakRef is null)
                    {
                        // First poll: resolve via locator and cache a weak reference.
                        var root = this.GetEffectiveRootTarget();
                        resolved = this.locatorResolver.TryResolve(locator, root);
                        if (resolved is not null)
                        {
                            // Retrieve the weak reference from the registry (the locator
                            // resolver already called GetOrCreateId, so the entry exists).
                            var nodeId = this.nodeRegistry.GetExistingId(resolved);
                            if (nodeId is not null)
                            {
                                elementWeakRef = this.nodeRegistry.TryGetWeakReference(nodeId);
                            }
                        }
                    }
                    else
                    {
                        // Subsequent polls: skip tree traversal — just dereference weak ref.
                        resolved = elementWeakRef.TryGetTarget(out var target) ? target : null;
                    }

                    if (resolved is null)
                    {
                        return new WaitForPropertyPollResult(false, null);
                    }

                    var props = PropertyInformation.GetProperties(resolved);
                    try
                    {
                        var match = props.FirstOrDefault(p =>
                            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                        return new WaitForPropertyPollResult(true, match?.StringValue);
                    }
                    finally
                    {
                        foreach (var prop in props)
                        {
                            prop.Teardown();
                            StopChangeTimer(prop);
                        }
                    }
                }, ct).ConfigureAwait(false);
                elementFound = pollResult.Found;
                actualValue = pollResult.Value;
            }
            catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
            {
                dispatcherBusy = true;
            }

            if (!dispatcherBusy)
            {
                bool conditionMet = absent
                    ? !elementFound
                    : elementFound && string.Equals(actualValue, expectedValue, StringComparison.Ordinal);

                if (conditionMet)
                {
                    return new Contracts.Dtos.WaitForPropertyResultDto
                    {
                        ConditionMet = true,
                        ActualValue = actualValue,
                        ElapsedMs = (int)sw.ElapsedMilliseconds,
                        PollCount = pollCount,
                    };
                }
            }

            if (sw.Elapsed >= deadline)
            {
                throw new SnoopException(
                    SnoopErrorCode.DispatcherBusy,
                    $"wpf_wait_for_property timed out after {timeoutMs}ms waiting for " +
                    $"'{propertyName}' {(absent ? "to disappear" : $"= \"{expectedValue}\"")}. " +
                    $"Last observed value: {(actualValue is null ? "<null>" : $"\"{actualValue}\"")}.",
                    suggestions: new[] { SnoopSuggestions.WaitForPropertyTimeout });
            }

            var remaining = (int)(deadline - sw.Elapsed).TotalMilliseconds;
            var delay = Math.Min(MinPollIntervalMs, Math.Max(1, remaining - 1));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    // ── M2-11: wpf_pump_until_idle ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Contracts.Dtos.PumpUntilIdleResultDto> PumpUntilIdleAsync(
        int timeoutMs,
        IReadOnlyList<string>? resources,
        CancellationToken ct)
    {
        this.ThrowIfDisposed();

        // Nested-pump guard (PRD §8.2): reject concurrent calls regardless of thread.
        // Interlocked.CompareExchange atomically sets pumpInProgress to 1 if it was 0.
        if (Interlocked.CompareExchange(ref this.pumpInProgress, 1, 0) != 0)
        {
            throw new SnoopException(
                SnoopErrorCode.DispatcherBusy,
                "wpf_pump_until_idle cannot be called re-entrantly: a pump is already in progress.",
                suggestions: new[] { SnoopSuggestions.DispatcherBusy });
        }

        // 5-second animation-runaway ceiling (PRD §8.2 W3-H2).
        const int MaxTimeoutMs = 5000;
        var clampedTimeout = Math.Min(timeoutMs, MaxTimeoutMs);

        try
        {
            return await this.PumpUntilIdleCoreAsync(clampedTimeout, resources, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref this.pumpInProgress, 0);
        }
    }

    private async Task<Contracts.Dtos.PumpUntilIdleResultDto> PumpUntilIdleCoreAsync(
        int timeoutMs,
        IReadOnlyList<string>? resources,
        CancellationToken ct)
    {
        // Build the set of resource names to monitor (null/empty = all built-in resources).
        var filterNames = resources is { Count: > 0 }
            ? new HashSet<string>(resources, StringComparer.OrdinalIgnoreCase)
            : null;

        // Instantiate built-in resources on the Dispatcher thread so they can observe it.
        // Only DispatcherIdlingResource and CompositionRenderingResource can be created without
        // a specific object instance to observe — they monitor the Dispatcher queue and
        // rendering pipeline respectively.
        var builtInResources = await this.RunOnDispatcherAsync<List<IIdlingResource>>(() =>
        {
            var list = new List<IIdlingResource>
            {
                new DispatcherIdlingResource(this.dispatcher),
                new CompositionRenderingResource(this.dispatcher),
            };

            // Apply resource name filter if specified.
            if (filterNames is not null)
            {
                list = list.FindAll(r => filterNames.Contains(r.Name));
            }

            return list;
        }, ct).ConfigureAwait(false);

        var monitored = builtInResources.ConvertAll(r => r.Name);

        // Build an IdlingResourceRegistry for the AND-gate.
        using var registry = new IdlingResourceRegistry();
        foreach (var r in builtInResources)
        {
            registry.Register(r);
        }

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(timeoutMs);

        try
        {
            // Fast path: already idle.
            if (registry.IsIdle)
            {
                return new Contracts.Dtos.PumpUntilIdleResultDto
                {
                    IdleReached = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ResourcesMonitored = monitored,
                    StillBusy = new List<string>(),
                };
            }

            // Wait for the registry IdleChanged event or timeout.
            using var idleSignal = new SemaphoreSlim(0, 1);

            void OnIdleChanged(object? sender, IdleChangedEventArgs e)
            {
                if (e.IsIdle)
                {
                    try
                    {
                        idleSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // Already signalled — ignore.
                    }
                    catch (ObjectDisposedException)
                    {
                        // idleSignal was disposed (cancellation raced with idle-change) — ignore.
                    }
                }
            }

            registry.IdleChanged += OnIdleChanged;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check again after subscribing to avoid a TOCTOU race.
                    if (registry.IsIdle)
                    {
                        return new Contracts.Dtos.PumpUntilIdleResultDto
                        {
                            IdleReached = true,
                            ElapsedMs = (int)sw.ElapsedMilliseconds,
                            ResourcesMonitored = monitored,
                            StillBusy = new List<string>(),
                        };
                    }

                    var remaining = (int)(deadline - sw.Elapsed).TotalMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    // Wait up to remaining ms for an idle signal.
                    var signalled = await idleSignal.WaitAsync(remaining, ct).ConfigureAwait(false);
                    if (signalled && registry.IsIdle)
                    {
                        return new Contracts.Dtos.PumpUntilIdleResultDto
                        {
                            IdleReached = true,
                            ElapsedMs = (int)sw.ElapsedMilliseconds,
                            ResourcesMonitored = monitored,
                            StillBusy = new List<string>(),
                        };
                    }

                    // Check elapsed again (handles the case where we were woken but not yet fully idle).
                    if (sw.Elapsed >= deadline)
                    {
                        break;
                    }
                }
            }
            finally
            {
                registry.IdleChanged -= OnIdleChanged;
            }

            // Timeout reached — identify still-busy resources and throw.
            var stillBusy = builtInResources.FindAll(r => !r.IsIdle).ConvertAll(r => r.Name);

            throw new SnoopException(
                SnoopErrorCode.DispatcherBusy,
                $"wpf_pump_until_idle timed out after {timeoutMs}ms waiting for idle. " +
                $"Still busy: [{string.Join(", ", stillBusy)}].",
                suggestions: new[] { SnoopSuggestions.DispatcherBusy });
        }
        finally
        {
            // Dispose all built-in resource instances we created.
            foreach (var r in builtInResources)
            {
                if (r is IDisposable d)
                {
                    d.Dispose();
                }
            }
        }
    }

    // ── M2-10: wpf_poll_changes ────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Contracts.Dtos.PollChangesResultDto> PollChangesAsync(
        long sinceVersion,
        WpfLocator? rootLocator,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var currentVersion = this.nodeRegistry.Version;

            // FX6-A6: Fast path — skip the O(N) tree walk when the registry version has not
            // changed since the last poll.
            //
            // Rationale: nodeRegistry.Version increments on every GetOrCreateId call.  If
            // Version == sinceVersion, no new nodes have been registered since the caller's
            // baseline, which means the "added" set is definitively empty.  We still need to
            // detect "removed" nodes (GC-collected objects), but we can do that in O(|live|)
            // by probing each previously-live node's WeakReference rather than re-walking the
            // entire visual tree.
            if (currentVersion == sinceVersion
                && this.lastPollVersion == sinceVersion
                && this.lastPollLiveIds is not null
                && rootLocator is null) // Fast path only applies to full-tree polls; scoped polls always walk.
            {
                // "added" = empty (version unchanged ⇒ no new registrations).
                // "removed" = previously-live nodes whose WeakReference has been collected.
                var changes = new List<Contracts.Dtos.NodeChangeEntryDto>();
                var stillLive = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

                foreach (var nodeId in this.lastPollLiveIds)
                {
                    var weakRef = this.nodeRegistry.TryGetWeakReference(nodeId);
                    if (weakRef is not null && weakRef.TryGetTarget(out _))
                    {
                        stillLive.Add(nodeId);
                    }
                    else
                    {
                        changes.Add(new Contracts.Dtos.NodeChangeEntryDto
                        {
                            NodeId = nodeId,
                            ChangeKind = "removed",
                        });
                    }
                }

                // Update snapshot with only the still-live nodes.
                this.lastPollVersion = currentVersion;
                this.lastPollLiveIds = stillLive;

                return new Contracts.Dtos.PollChangesResultDto
                {
                    TreeVersion = currentVersion,
                    SinceVersion = sinceVersion,
                    Changes = changes,
                    ChangeCount = changes.Count,
                };
            }

            // ── Standard path: O(N) tree walk ─────────────────────────────────────────────

            // Walk the live visual tree from the effective root (or locator root).
            var rootTarget = rootLocator is not null
                ? (object?)this.locatorResolver.TryResolve(rootLocator, this.GetEffectiveRootTarget())
                : this.GetEffectiveRootTarget();

            // Collect all nodeIds currently visible in the live tree.
            // CollectLiveNodeIds uses GetExistingId — does NOT register new nodes or
            // increment the version counter. Only nodes already known to the registry
            // (registered by prior operations such as GetVisualTree, FindElements, etc.)
            // appear in liveNodeIds. This keeps the version stable across back-to-back
            // polls when no external mutations have occurred.
            var liveNodeIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (rootTarget is not null)
            {
                this.CollectLiveNodeIds(rootTarget, liveNodeIds);
            }

            // Snapshot version after the walk (GetExistingId does not increment, so
            // this equals Version before the walk). Used as the returned baseline.
            currentVersion = this.nodeRegistry.Version;

            // "added" = in live tree AND registered after sinceVersion.
            var standardChanges = new List<Contracts.Dtos.NodeChangeEntryDto>();

            foreach (var nodeId in liveNodeIds)
            {
                if (TryParseNodeVersion(nodeId, out var nodeVersion)
                    && nodeVersion > sinceVersion)
                {
                    standardChanges.Add(new Contracts.Dtos.NodeChangeEntryDto
                    {
                        NodeId = nodeId,
                        ChangeKind = "added",
                    });
                }
            }

            // "removed" — prefer snapshot-based diff when we have a cached live set from the
            // previous poll at exactly sinceVersion (incremental case).  Fall back to the
            // registry-scan (CollectRemovedSince) for the initial / catch-up case.
            IEnumerable<string> removedIds;
            if (this.lastPollVersion == sinceVersion && this.lastPollLiveIds is not null)
            {
                // Incremental: anything that was alive last time but is not alive now.
                removedIds = this.lastPollLiveIds
                    .Where(id => !liveNodeIds.Contains(id))
                    .ToList();
            }
            else
            {
                // Catch-up: fall back to registry scan for nodes registered <= sinceVersion.
                removedIds = this.nodeRegistry.CollectRemovedSince(sinceVersion, liveNodeIds);
            }

            foreach (var removedId in removedIds)
            {
                standardChanges.Add(new Contracts.Dtos.NodeChangeEntryDto
                {
                    NodeId = removedId,
                    ChangeKind = "removed",
                });
            }

            // Cache this poll's live snapshot so the next incremental poll can diff against it.
            this.lastPollVersion = currentVersion;
            this.lastPollLiveIds = liveNodeIds;

            return new Contracts.Dtos.PollChangesResultDto
            {
                TreeVersion = currentVersion,
                SinceVersion = sinceVersion,
                Changes = standardChanges,
                ChangeCount = standardChanges.Count,
            };
        }, ct);
    }

    /// <summary>
    /// Parses the sequence number from a node ID in the format "0:&lt;n&gt;".
    /// </summary>
    private static bool TryParseNodeVersion(string nodeId, out long version)
    {
        version = 0;
        // Use string overload to satisfy CA1307 across all TFMs.
        var colonIdx = nodeId.IndexOf(":", StringComparison.Ordinal);
        if (colonIdx < 0)
        {
            return false;
        }

        return long.TryParse(nodeId.Substring(colonIdx + 1), out version);
    }

    /// <summary>
    /// Recursively walks the visual tree from <paramref name="root"/> and collects
    /// stable node IDs for all reachable objects that are already registered in the
    /// NodeRegistry. Must be called on the Dispatcher thread.
    ///
    /// Deliberately uses <see cref="NodeRegistry.GetExistingId"/> (not GetOrCreateId)
    /// so that the tree walk does NOT increment the version counter or register new
    /// nodes. This keeps the treeVersion stable across back-to-back poll calls when
    /// the tree has not actually mutated.
    /// </summary>
    private void CollectLiveNodeIds(object root, System.Collections.Generic.HashSet<string> ids)
    {
        const int maxNodes = 5000;
        // visitedObjects prevents re-queuing the same object even when it has no
        // registered ID yet (avoids infinite loops through unregistered subtrees).
#if NET5_0_OR_GREATER
        var visitedObjects = new System.Collections.Generic.HashSet<object>(
            ReferenceEqualityComparer.Instance);
#else
        var visitedObjects = new System.Collections.Generic.HashSet<object>(
            ObjectReferenceEqualityComparer.Instance);
#endif
        var queue = new Queue<object>();
        queue.Enqueue(root);

        while (queue.Count > 0 && ids.Count < maxNodes)
        {
            var current = queue.Dequeue();
            if (current is null || !visitedObjects.Add(current))
            {
                continue;
            }

            // Only include nodes that are already in the registry.
            // GetExistingId does NOT create new registrations.
            var id = this.nodeRegistry.GetExistingId(current);
            if (id is not null)
            {
                ids.Add(id);
            }

            // Walk visual children.
            if (current is System.Windows.Media.Visual visual)
            {
                var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual);
                for (var i = 0; i < childCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(visual, i);
                    if (child is not null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            else if (current is System.Windows.Application app)
            {
                foreach (System.Windows.Window w in app.Windows)
                {
                    if (w is not null)
                    {
                        queue.Enqueue(w);
                    }
                }
            }
        }
    }

}
