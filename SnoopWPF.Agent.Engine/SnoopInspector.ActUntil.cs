// SnoopInspector.ActUntil.cs
// wpf_act_until — fire one mutation primitive, then poll a property predicate until matched
// or the deadline passes. Replaces "click + wait_for_property" round-trip pairs.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Snoop.Infrastructure;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <content/>
public sealed partial class SnoopInspector
{
    private const int ActUntilMinPollIntervalMs = 50;

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

                    var props = PropertyInformation.GetProperties(resolved);
                    try
                    {
                        var match = props.FirstOrDefault(p =>
                            string.Equals(p.Name, predicate.PropertyName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.DisplayName, predicate.PropertyName, StringComparison.OrdinalIgnoreCase));

                        return new ActUntilPollResult(true, match?.StringValue);
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
            var delay = Math.Min(ActUntilMinPollIntervalMs, Math.Max(1, remaining - 1));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private readonly record struct ActUntilPollResult(bool Found, string? Value);
}
