// SnoopInspector.ActSequence.cs
// wpf_act_sequence — server-side execution of an ordered list of L0/L1 mutation primitives.
// Collapses N LLM round-trips into one for multi-step navigation flows.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <content/>
public sealed partial class SnoopInspector
{
    /// <inheritdoc/>
    public async Task<ActionSequenceResultDto> ExecuteActionSequenceAsync(
        List<ActionStepDto> steps,
        bool stopOnError,
        CancellationToken ct)
    {
        if (steps is null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        var result = new ActionSequenceResultDto
        {
            AllSucceeded = true,
            StoppedAtIndex = -1,
            Steps = new List<ActionStepResultDto>(steps.Count),
        };

        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[i];
            var stepResult = await this.ExecuteOneStepAsync(i, step, ct).ConfigureAwait(false);
            result.Steps.Add(stepResult);

            if (!stepResult.Success)
            {
                result.AllSucceeded = false;
                if (result.StoppedAtIndex < 0)
                {
                    result.StoppedAtIndex = i;
                }

                if (stopOnError)
                {
                    return result;
                }
            }
        }

        return result;
    }

    private async Task<ActionStepResultDto> ExecuteOneStepAsync(
        int index,
        ActionStepDto step,
        CancellationToken ct)
    {
        var resultShell = new ActionStepResultDto
        {
            Index = index,
            Type = step.Type ?? string.Empty,
            NodeId = step.NodeId ?? string.Empty,
        };

        try
        {
            var nodeId = step.NodeId ?? string.Empty;
            var stepType = (step.Type ?? string.Empty).ToUpperInvariant();
            StateDeltaDto delta;
            switch (stepType)
            {
                case "CLICK":
                    delta = await this.ClickAsync(nodeId, ct).ConfigureAwait(false);
                    break;
                case "DOUBLE_CLICK":
                    delta = await this.DoubleClickAsync(nodeId, ct).ConfigureAwait(false);
                    break;
                case "EXECUTE_COMMAND":
                    delta = await this.ExecuteCommandAsync(nodeId, ct).ConfigureAwait(false);
                    break;
                case "SET_TEXT":
                    delta = await this.SetTextValueAsync(nodeId, step.Value ?? string.Empty, ct).ConfigureAwait(false);
                    break;
                default:
                    resultShell.Success = false;
                    resultShell.ErrorCode = SnoopErrorCode.InvalidArgument.ToString();
                    resultShell.ErrorMessage =
                        $"Unknown action type '{step.Type}'. Supported: click, double_click, execute_command, set_text.";
                    return resultShell;
            }

            resultShell.Success = delta.Success;
            resultShell.Delta = delta;
            if (!delta.Success)
            {
                // Surface the underlying failure reason so the caller doesn't need to dig into the delta.
                resultShell.ErrorCode = delta.FailureReason?.ToString() ?? "UNKNOWN";
                resultShell.ErrorMessage = delta.ActionabilityChecksFailed is { Count: > 0 }
                    ? string.Join(", ", delta.ActionabilityChecksFailed)
                    : null;
            }

            return resultShell;
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation up — sequence-level CT.
            throw;
        }
        catch (SnoopException sx)
        {
            resultShell.Success = false;
            resultShell.ErrorCode = sx.Code.ToString();
            resultShell.ErrorMessage = sx.Message;
            return resultShell;
        }
        catch (Exception ex)
        {
            resultShell.Success = false;
            resultShell.ErrorCode = "UNHANDLED";
            resultShell.ErrorMessage = ex.Message;
            return resultShell;
        }
    }
}
