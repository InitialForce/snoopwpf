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
using Snoop.Data.Tree;
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

            // Visual tree is the right surface here: actionables care about what's actually
            // rendered on screen, not the logical/automation projection.
            using var treeService = TreeService.From(TreeType.Visual);
            var rootItem = treeService.Construct(root, parent: null);
            if (rootItem is null)
            {
                return new ActionablesResultDto();
            }

            var items = new List<ActionableDto>();
            var totalScanned = 0;
            var truncated = false;

            var queue = new Queue<TreeItem>();
            queue.Enqueue(rootItem);

            while (queue.Count > 0 && !truncated)
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
                if (current.Target is UIElement ui && (!ui.IsVisible || ui.Visibility != Visibility.Visible))
                {
                    continue;
                }

                foreach (var child in current.Children)
                {
                    queue.Enqueue(child);
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
    /// Returns true and emits a populated <paramref name="dto"/> when <paramref name="item"/>
    /// is a visible, enabled control that an LLM can interact with.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private static bool TryProjectActionable(TreeItem item, NodeRegistry nodeRegistry, out ActionableDto dto)
    {
        dto = null!;

        if (item.Target is not DependencyObject depObj)
        {
            return false;
        }

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
            NodeId = nodeRegistry.GetOrCreateId(item.Target),
            Kind = kind,
            Label = ResolveLabel(depObj),
            Name = xname,
            AutomationId = automationId,
            TypeName = item.TargetType?.Name ?? depObj.GetType().Name,
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
    /// Cheap variant of <see cref="HasCommandBinding"/> for the actionables walk —
    /// only checks the local value, not BindingExpression resolution.
    /// </summary>
    private static bool HasCommandBindingFast(DependencyObject d)
    {
        if (d is not ButtonBase)
        {
            // Command property is declared on ButtonBase + MenuItem + Hyperlink.
            // Fast path covers ButtonBase; fall through for the others.
        }

        var cmd = d.GetValue(ButtonBase.CommandProperty);
        if (cmd != null)
        {
            return true;
        }

        // Some controls define their own Command DPs (MenuItem.Command, Hyperlink.Command).
        if (d is MenuItem mi && mi.Command != null)
        {
            return true;
        }

        if (d is Hyperlink hl && hl.Command != null)
        {
            return true;
        }

        return false;
    }
}
