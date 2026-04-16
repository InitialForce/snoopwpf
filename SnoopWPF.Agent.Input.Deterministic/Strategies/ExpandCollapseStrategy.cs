namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L1 strategy: expands or collapses a WPF element via
/// <see cref="UIElementAutomationPeer"/> / <see cref="IExpandCollapseProvider"/>.
/// </summary>
/// <remarks>
/// This strategy uses the UI Automation <see cref="IExpandCollapseProvider"/> pattern,
/// which maps to the element's native expand/collapse behaviour without raw Win32 input.
/// Supported target controls include <c>TreeViewItem</c>, <c>Expander</c>, and
/// <c>GroupItem</c>, as well as any element whose <see cref="AutomationPeer"/> supports
/// <see cref="PatternInterface.ExpandCollapse"/>.
///
/// The <c>action</c> argument must be either <c>"expand"</c> or <c>"collapse"</c>
/// (case-insensitive). Passing an unrecognised action returns
/// <see cref="FailureReason.InvalidArgument"/>.
///
/// Gate: <see cref="InputIntentKind.ExpandCollapse"/> requires automation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class ExpandCollapseStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L1;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when:
    /// <list type="bullet">
    ///   <item>The intent kind is <see cref="InputIntentKind.ExpandCollapse"/>.</item>
    ///   <item>The target is a <see cref="UIElement"/>.</item>
    ///   <item>An <see cref="AutomationPeer"/> can be created for the element.</item>
    ///   <item>The peer supports <see cref="PatternInterface.ExpandCollapse"/>.</item>
    /// </list>
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.ExpandCollapse)
        {
            return false;
        }

        if (target is not UIElement uiElement)
        {
            return false;
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        return peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        if (target is not UIElement uiElement)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        // Resolve the "action" argument ("expand" or "collapse").
        string? action = null;
        foreach (var arg in intent.Arguments)
        {
            if (string.Equals(arg.Name, "action", System.StringComparison.OrdinalIgnoreCase))
            {
                action = arg.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(action) ||
            (!string.Equals(action, "expand", System.StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(action, "collapse", System.StringComparison.OrdinalIgnoreCase)))
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        var provider = peer?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;

        if (provider is null)
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.PatternNotSupported,
                ChosenTier = InputTier.L1,
            };
        }

        bool expand = string.Equals(action, "expand", System.StringComparison.OrdinalIgnoreCase);
        if (expand)
        {
            provider.Expand();
        }
        else
        {
            provider.Collapse();
        }

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] ExpandCollapseStrategy.Invoke: type={uiElement.GetType().Name}, action={action}");

        return new DeterministicInputResult
        {
            Success = true,
            ChosenTier = InputTier.L1,
        };
    }
}
