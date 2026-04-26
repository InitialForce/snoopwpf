// SnoopInspector.ActUntil.cs
// wpf_act_until — fire one mutation primitive, then poll a property predicate until matched
// or the deadline passes. Replaces "click + wait_for_property" round-trip pairs.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <content/>
public sealed partial class SnoopInspector
{
    // Adaptive polling cadence: a fast first beat catches predicates that match in <50ms
    // (the common "click → IsEnabled flips immediately" case); subsequent intervals double
    // up to a cap so a long wait doesn't burn CPU. With 25/50/100/100ms… cadence we hit the
    // 1-second mark in 6 polls instead of 20, and after that match the previous flat 50ms.
    private const int ActUntilFirstPollIntervalMs = 25;
    private const int ActUntilMaxPollIntervalMs = 100;

    /// <inheritdoc/>
    public async Task<ActUntilResultDto> ActUntilAsync(
        ActionStepDto action,
        ActUntilPredicateDto predicate,
        int timeoutMs,
        CancellationToken ct)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (string.IsNullOrEmpty(predicate.TargetNodeId))
        {
            throw new SnoopException(
                SnoopErrorCode.InvalidArgument,
                "act_until predicate requires a non-empty TargetNodeId.");
        }

        if (string.IsNullOrEmpty(predicate.PropertyName))
        {
            throw new SnoopException(
                SnoopErrorCode.InvalidArgument,
                "act_until predicate requires a non-empty PropertyName.");
        }

        // Step 1: fire the action via the same dispatcher as ExecuteOneStep.
        var actionResult = await this.ExecuteOneStepAsync(0, action, ct).ConfigureAwait(false);

        var result = new ActUntilResultDto
        {
            ActionResult = actionResult,
            Success = false,
            PredicateMet = false,
            TimedOut = false,
        };

        if (!actionResult.Success)
        {
            // The action itself failed — short-circuit. Still report so the LLM sees why.
            return result;
        }

        // Step 2: poll the predicate until matched or deadline.
        var absent = string.Equals(predicate.PresenceExpected, "absent", StringComparison.OrdinalIgnoreCase);
        var deadline = TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));
        var sw = Stopwatch.StartNew();

        // Cache a weak reference once so subsequent polls don't re-walk the registry.
        WeakReference<object>? targetWeak = null;

        // Adaptive cadence: starts at ActUntilFirstPollIntervalMs and doubles up to
        // ActUntilMaxPollIntervalMs. The first poll fires immediately (no leading delay).
        var pollInterval = ActUntilFirstPollIntervalMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            result.PollCount++;

            string? actualValue = null;
            bool elementFound = false;
            bool dispatcherBusy = false;

            try
            {
                var pollResult = await this.RunOnDispatcherAsync(() =>
                {
                    object? resolved;
                    if (targetWeak is null)
                    {
                        targetWeak = this.nodeRegistry.TryGetWeakReference(predicate.TargetNodeId);
                        resolved = targetWeak is not null && targetWeak.TryGetTarget(out var first) ? first : null;
                    }
                    else
                    {
                        resolved = targetWeak.TryGetTarget(out var t) ? t : null;
                    }

                    if (resolved is null)
                    {
                        return new ActUntilPollResult(false, null);
                    }

                    return TryReadStringValue(resolved, predicate.PropertyName, out var stringValue)
                        ? new ActUntilPollResult(true, stringValue)
                        : new ActUntilPollResult(true, null);
                }, ct).ConfigureAwait(false);

                elementFound = pollResult.Found;
                actualValue = pollResult.Value;
            }
            catch (SnoopException ex) when (ex.Code == SnoopErrorCode.DispatcherBusy)
            {
                dispatcherBusy = true;
            }

            result.ActualValue = actualValue;

            if (!dispatcherBusy)
            {
                bool conditionMet = absent
                    ? !elementFound
                    : elementFound && string.Equals(actualValue, predicate.ExpectedValue, StringComparison.Ordinal);

                if (conditionMet)
                {
                    result.PredicateMet = true;
                    result.Success = true;
                    result.ElapsedMs = (int)sw.ElapsedMilliseconds;
                    return result;
                }
            }

            if (sw.Elapsed >= deadline)
            {
                result.TimedOut = true;
                result.ElapsedMs = (int)sw.ElapsedMilliseconds;
                return result;
            }

            var remaining = (int)(deadline - sw.Elapsed).TotalMilliseconds;
            var delay = Math.Min(pollInterval, Math.Max(1, remaining - 1));
            await Task.Delay(delay, ct).ConfigureAwait(false);

            // Back off the next interval: double up to ActUntilMaxPollIntervalMs.
            // Cheap UIs match in 1-2 polls; long waits don't burn CPU on a tight 50ms loop.
            pollInterval = Math.Min(pollInterval * 2, ActUntilMaxPollIntervalMs);
        }
    }

    private readonly record struct ActUntilPollResult(bool Found, string? Value);

    // ── Fast property reader for poll loops ────────────────────────────────
    //
    // The original implementation called Snoop's PropertyInformation.GetProperties
    // on every poll, which allocates ~100 PropertyInformation wrappers (each
    // wiring up PropertyChanged listeners) just to read one value by name.
    // For a 5-second wait at 50ms polling, that's ~10 000 allocations + 100
    // sort operations + 100 binding-wire-up/teardown cycles.
    //
    // The reader below resolves a (Type, propertyName) pair to either a
    // DependencyProperty or a CLR PropertyInfo once, caches the resolution
    // forever (process-lifetime; types are GC roots in practice for any UI
    // that's actually live), and reads the value directly via GetValue.
    // Mirrors PropertyInformation.StringValue semantics:
    //     value?.ToString() ?? string.Empty.
    //
    // Worst case for a brand-new (Type, name) pair: one DPD lookup + one
    // PropertyInfo reflection call. Steady state: a single dictionary read +
    // one GetValue / PropertyInfo.GetValue call.
    // Keyed by Type-then-name to keep net462 (no ValueTuple in framework) happy.
    // The outer cache maps Type → inner cache; inner cache is a per-type Dictionary
    // protected by its own lock. Two locks deep is plenty for our access pattern
    // (a single act_until call repeats the same key in a hot loop).
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, PropertyAccessor>> AccessorCache
        = new();

    /// <summary>
    /// Test-visible direct-read implementation for the act_until poll loop.
    /// Returns <see langword="true"/> when the property exists on <paramref name="target"/>
    /// and was read successfully; <paramref name="value"/> mirrors
    /// <c>PropertyInformation.StringValue</c> semantics (raw value's <see cref="object.ToString"/>
    /// or <see cref="string.Empty"/> when the value is null).
    /// </summary>
    internal static bool TryReadStringValue(object target, string propertyName, out string? value)
    {
        var type = target.GetType();
        var perType = AccessorCache.GetOrAdd(type, static _
            => new ConcurrentDictionary<string, PropertyAccessor>(StringComparer.OrdinalIgnoreCase));

        // Two-step lookup so the steady-state hot path allocates nothing: net462's
        // ConcurrentDictionary lacks the (key, factory, arg) GetOrAdd overload.
        if (!perType.TryGetValue(propertyName, out var accessor))
        {
            accessor = PropertyAccessor.Resolve(type, propertyName);
            perType.TryAdd(propertyName, accessor);
        }

        if (accessor.Kind == PropertyAccessorKind.None)
        {
            value = null;
            return false;
        }

        try
        {
            var raw = accessor.Read(target);
            value = raw?.ToString() ?? string.Empty;
            return true;
        }
        catch
        {
            // Mirror PropertyInformation.GetProperties' tolerant behaviour: a property that
            // throws on read is reported as absent rather than crashing the poll loop.
            value = null;
            return false;
        }
    }

    private enum PropertyAccessorKind
    {
        None,
        DependencyProperty,
        ClrProperty,
    }

    private readonly struct PropertyAccessor
    {
        public PropertyAccessorKind Kind { get; }

        private readonly DependencyProperty? dp;
        private readonly PropertyInfo? clr;

        private PropertyAccessor(PropertyAccessorKind kind, DependencyProperty? dp, PropertyInfo? clr)
        {
            this.Kind = kind;
            this.dp = dp;
            this.clr = clr;
        }

        public static PropertyAccessor Resolve(Type type, string propertyName)
        {
            // 1. DependencyPropertyDescriptor handles attached + standard DPs and matches
            //    by descriptor Name (case-insensitive via TypeDescriptor's collection lookup).
            var descriptors = TypeDescriptor.GetProperties(type);
            for (int i = 0; i < descriptors.Count; i++)
            {
                var pd = descriptors[i];
                if (!string.Equals(pd.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pd.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dpd = DependencyPropertyDescriptor.FromProperty(pd);
                if (dpd?.DependencyProperty is { } dp)
                {
                    return new PropertyAccessor(PropertyAccessorKind.DependencyProperty, dp, null);
                }
            }

            // 2. CLR property fallback (covers properties that aren't backed by a DP, like
            //    PasswordBox.Password, anything declared as a plain auto-property, etc.).
            var clr = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (clr is not null && clr.CanRead)
            {
                return new PropertyAccessor(PropertyAccessorKind.ClrProperty, null, clr);
            }

            return new PropertyAccessor(PropertyAccessorKind.None, null, null);
        }

        public object? Read(object target)
        {
            return this.Kind switch
            {
                PropertyAccessorKind.DependencyProperty when target is DependencyObject d
                    => d.GetValue(this.dp!),
                PropertyAccessorKind.ClrProperty
                    => this.clr!.GetValue(target),
                _ => null,
            };
        }
    }
}
