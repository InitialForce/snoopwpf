// SnoopInspector.Actionables.cs
// wpf_get_actionables — compact, LLM-optimized list of "what you can interact with right now".

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <content/>
public sealed partial class SnoopInspector
{
    /// <inheritdoc/>
    public Task<ActionablesResultDto> GetActionablesAsync(
        string? rootNodeId,
        int maxResults,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            // Cap at 200 — same ceiling as wpf_find_elements; avoids unbounded payloads.
            var cap = Math.Min(maxResults <= 0 ? 100 : maxResults, 200);

            var root = this.ResolveRootTarget(rootNodeId);
            if (root is not DependencyObject rootDep)
            {
                return new ActionablesResultDto();
            }

            // When the root's owning window is disabled by an open modal dialog the walk below
            // finds no enabled controls, so the result is empty and indistinguishable from a
            // genuinely empty screen. Emit a MODAL_BLOCKED warning through the same channel the
            // interaction sites use so callers can tell the two apart. No modal => no warning,
            // preserving empty-vs-empty semantics for an actually-empty screen.
            WarnIfModallyBlocked(rootDep);

            // Visual tree is the right surface here: actionables care about what's actually
            // rendered on screen, not the logical/automation projection. We walk via
            // VisualTreeHelper directly instead of building Snoop's TreeItem wrappers — for
            // a typical 2-3k-node MC view that saves 2-3k throwaway TreeItem allocations
            // per call (rc.6 → rc.8 perf opt #2).
            var items = new List<ActionableDto>(cap);
            var totalScanned = 0;
            var truncated = false;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(rootDep);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                totalScanned++;

                if (TryProjectActionable(current, this.nodeRegistry, out var dto))
                {
                    if (items.Count >= cap)
                    {
                        truncated = true;
                        break;
                    }

                    items.Add(dto);
                }

                // Skip subtrees of invisible / collapsed parents — they contain no actionables.
                if (current is UIElement ui && (!ui.IsVisible || ui.Visibility != Visibility.Visible))
                {
                    continue;
                }

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                }
            }

            return new ActionablesResultDto
            {
                Items = items,
                TotalScanned = totalScanned,
                Truncated = truncated,
            };
        }, ct);
    }

    /// <summary>
    /// Returns true and emits a populated <paramref name="dto"/> when <paramref name="depObj"/>
    /// is a visible, enabled control that an LLM can interact with.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private static bool TryProjectActionable(DependencyObject depObj, NodeRegistry nodeRegistry, out ActionableDto dto)
    {
        dto = null!;

        var kind = ClassifyKind(depObj);
        if (kind is null)
        {
            return false;
        }

        // Visibility + size: skip controls that aren't on screen.
        if (depObj is UIElement ui)
        {
            if (!ui.IsVisible || ui.Visibility != Visibility.Visible)
            {
                return false;
            }

            if (ui is FrameworkElement feSize && (feSize.ActualWidth <= 0 || feSize.ActualHeight <= 0))
            {
                return false;
            }
        }

        // Enabled gate.
        bool isEnabled = depObj is UIElement uie ? uie.IsEnabled : true;

        var automationId = AutomationProperties.GetAutomationId(depObj) ?? string.Empty;
        var fe = depObj as FrameworkElement;
        var xname = fe?.Name ?? string.Empty;

        // Mirror WPF UIA fallback: AutomationId reports x:Name when the attached property is unset.
        if (string.IsNullOrEmpty(automationId) && !string.IsNullOrEmpty(xname))
        {
            automationId = xname;
        }

        dto = new ActionableDto
        {
            NodeId = nodeRegistry.GetOrCreateId(depObj),
            Kind = kind,
            Label = ResolveLabel(depObj),
            Name = xname,
            AutomationId = automationId,
            TypeName = depObj.GetType().Name,
            IsEnabled = isEnabled,
            HasCommandBinding = HasCommandBindingFast(depObj),
        };

        return true;
    }

    /// <summary>Coarse interaction kind, or <see langword="null"/> when not actionable.</summary>
    private static string? ClassifyKind(DependencyObject d)
    {
        // Order matters: more specific types first.
        return d switch
        {
            CheckBox => "checkbox",
            RadioButton => "radio",
            ToggleButton => "toggle",
            // ButtonBase covers Button, RepeatButton, GridViewColumnHeader, etc.
            ButtonBase => "button",
            MenuItem => "menuitem",
            Hyperlink => "hyperlink",
            ComboBox => "combobox",
            TabItem => "tab",
            // Selector items (ListBoxItem, ComboBoxItem, ListViewItem) — addressable, clickable.
            ListBoxItem => "listitem",
            TreeViewItem => "expander",
            Expander => "expander",
            Slider => "slider",
            PasswordBox => "input",
            // TextBoxBase covers TextBox, RichTextBox.
            TextBoxBase => "input",
            _ => null,
        };
    }

    /// <summary>
    /// Best-effort label. Resolution order matches the contract on <see cref="ActionableDto.Label"/>.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private static string ResolveLabel(DependencyObject d)
    {
        var apName = AutomationProperties.GetName(d);
        if (!string.IsNullOrWhiteSpace(apName))
        {
            return apName.Trim();
        }

        var apHelp = AutomationProperties.GetHelpText(d);
        if (!string.IsNullOrWhiteSpace(apHelp))
        {
            return apHelp.Trim();
        }

        if (d is HeaderedContentControl hc && hc.Header is string hs && !string.IsNullOrWhiteSpace(hs))
        {
            return hs.Trim();
        }

        if (d is HeaderedItemsControl hi && hi.Header is string his && !string.IsNullOrWhiteSpace(his))
        {
            return his.Trim();
        }

        if (d is ContentControl cc && cc.Content is string cs && !string.IsNullOrWhiteSpace(cs))
        {
            return cs.Trim();
        }

        if (d is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            return tb.Text.Trim();
        }

        if (d is Hyperlink hl)
        {
            var inlineText = string.Concat(hl.Inlines.OfType<Run>().Select(r => r.Text));
            if (!string.IsNullOrWhiteSpace(inlineText))
            {
                return inlineText.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Cheap command-binding probe for the actionables walk — only checks the local value.
    /// </summary>
    /// <remarks>
    /// The <see cref="System.Windows.Input.ICommand"/>-bearing types differ:
    /// <see cref="ButtonBase.Command"/>, <see cref="MenuItem.Command"/>, and
    /// <see cref="Hyperlink.Command"/> are independent DPs. We dispatch on type instead
    /// of reading <see cref="ButtonBase.CommandProperty"/> on every <see cref="DependencyObject"/>
    /// (which would silently return null for non-ButtonBase types).
    /// </remarks>
    private static bool HasCommandBindingFast(DependencyObject d)
    {
        return d switch
        {
            ButtonBase bb => bb.Command is not null,
            MenuItem mi => mi.Command is not null,
            Hyperlink hl => hl.Command is not null,
            _ => false,
        };
    }
}
