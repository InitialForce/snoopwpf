// SnoopInspector.Triggers.cs
// FX6-A7: Trigger and behavior inspection.

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
    public Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var result = new List<TriggerDto>();

            var enableRedaction = this.options.EnableRedaction;

            // Style triggers (including base styles via BasedOn chain).
            if (target is FrameworkElement fe)
            {
                var style = Snoop.Infrastructure.Helpers.FrameworkElementHelper.GetStyle(fe);
                CollectStyleTriggers(fe, style, "Style", result, enableRedaction);

                // Element-level triggers (FrameworkElement.Triggers).
                foreach (System.Windows.TriggerBase tb in fe.Triggers)
                {
                    result.Add(ProjectTrigger(tb, "Element", enableRedaction));
                }

                // ControlTemplate triggers.
                if (Snoop.Infrastructure.Helpers.FrameworkElementHelper.GetTemplate(fe) is System.Windows.Controls.ControlTemplate ct2)
                {
                    foreach (System.Windows.TriggerBase tb in ct2.Triggers)
                    {
                        result.Add(ProjectTrigger(tb, "ControlTemplate", enableRedaction));
                    }
                }
            }
            else if (target is System.Windows.FrameworkContentElement fce)
            {
                var style = Snoop.Infrastructure.Helpers.FrameworkElementHelper.GetStyle(fce);
                CollectStyleTriggersForFce(fce, style, "Style", result, enableRedaction);
            }

            // DataTemplate triggers (ContentControl / ContentPresenter).
            if (target is System.Windows.Controls.ContentControl { ContentTemplate: { } contentTemplate })
            {
                foreach (System.Windows.TriggerBase tb in contentTemplate.Triggers)
                {
                    result.Add(ProjectTrigger(tb, "DataTemplate", enableRedaction));
                }
            }
            else if (target is System.Windows.Controls.ContentPresenter { ContentTemplate: { } cpTemplate })
            {
                foreach (System.Windows.TriggerBase tb in cpTemplate.Triggers)
                {
                    result.Add(ProjectTrigger(tb, "DataTemplate", enableRedaction));
                }
            }

            return result;
        }, ct);
    }

    /// <inheritdoc/>
    public Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var result = new List<BehaviorDto>();

            if (target is not DependencyObject depObj)
            {
                return result;
            }

            // Try both well-known Interaction libraries via reflection.
            // If the assembly isn't loaded, return empty (not an error).
            var enableRedaction = this.options.EnableRedaction;
            CollectBehaviorsFromInteraction(depObj, "System.Windows.Interactivity.Interaction, System.Windows.Interactivity", result, enableRedaction);
            CollectBehaviorsFromInteraction(depObj, "Microsoft.Xaml.Behaviors.Interaction, Microsoft.Xaml.Behaviors", result, enableRedaction);

            return result;
        }, ct);
    }

    public async Task<List<TriggerDto>> GetTriggersAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetTriggersAsync(nodeId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<BehaviorDto>> GetBehaviorsAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetBehaviorsAsync(nodeId, ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Trigger / Behavior helpers (called only from within Dispatcher.Invoke)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks the BasedOn chain of a FrameworkElement's Style and appends TriggerDtos.
    /// </summary>
    private static void CollectStyleTriggers(FrameworkElement instance, System.Windows.Style? style, string source, List<TriggerDto> result, bool enableRedaction)
    {
        var current = style;
        while (current is not null)
        {
            foreach (System.Windows.TriggerBase tb in current.Triggers)
            {
                result.Add(ProjectTrigger(tb, source, enableRedaction));
            }

            current = GetBaseStyle(instance, current);
        }
    }

    /// <summary>
    /// Walks the BasedOn chain of a FrameworkContentElement's Style and appends TriggerDtos.
    /// </summary>
    private static void CollectStyleTriggersForFce(FrameworkContentElement instance, System.Windows.Style? style, string source, List<TriggerDto> result, bool enableRedaction)
    {
        var current = style;
        while (current is not null)
        {
            foreach (System.Windows.TriggerBase tb in current.Triggers)
            {
                result.Add(ProjectTrigger(tb, source, enableRedaction));
            }

            // Walk BasedOn chain.
            current = current.BasedOn;
        }
    }

    /// <summary>
    /// Returns the base style for a FrameworkElement's style, including implicit base styles.
    /// Mirrors the logic in TriggersView.GetBaseStyle.
    /// </summary>
    private static System.Windows.Style? GetBaseStyle(FrameworkElement instance, System.Windows.Style style)
    {
        if (style.BasedOn is not null)
        {
            return style.BasedOn;
        }

        // Check if the style has an implicit base style via the internal IsBasedOnModified property.
        var value = StyleIsBasedOnModifiedPropertyInfo?.GetValue(style, null);
        if (value is true)
        {
            return instance.TryFindResource(style.TargetType) as System.Windows.Style;
        }

        return null;
    }

#pragma warning disable SA1310
    private static readonly PropertyInfo? StyleIsBasedOnModifiedPropertyInfo =
        typeof(System.Windows.Style).GetProperty("IsBasedOnModified", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore SA1310

    /// <summary>
    /// Projects a WPF TriggerBase to a TriggerDto.
    /// All WPF access happens synchronously on the Dispatcher (caller's responsibility).
    /// Does NOT use TypeDescriptor — values are obtained via .ToString() only.
    /// Applies redaction to DP-keyed condition and setter values when enableRedaction is true.
    /// </summary>
    private static TriggerDto ProjectTrigger(System.Windows.TriggerBase triggerBase, string source, bool enableRedaction)
    {
        var dto = new TriggerDto
        {
            Source = source,
            TriggerType = triggerBase.GetType().Name,
            IsActive = false,
            Conditions = new List<TriggerConditionDto>(),
            Setters = new List<TriggerSetterDto>(),
        };

        switch (triggerBase)
        {
            case System.Windows.Trigger t:
                dto.Conditions.Add(new TriggerConditionDto
                {
                    Property = $"{t.Property?.OwnerType?.Name}.{t.Property?.Name}",
                    Value = enableRedaction
                        ? RedactionFilter.Redact(t.Property?.Name ?? string.Empty, null, t.Value)
                        : t.Value?.ToString() ?? string.Empty,
                });
                foreach (System.Windows.SetterBase sb in t.Setters)
                {
                    if (sb is System.Windows.Setter s)
                    {
                        dto.Setters.Add(new TriggerSetterDto
                        {
                            Property = $"{s.Property?.OwnerType?.Name}.{s.Property?.Name}",
                            Value = enableRedaction
                                ? RedactionFilter.Redact(s.Property?.Name ?? string.Empty, null, s.Value)
                                : s.Value?.ToString() ?? string.Empty,
                        });
                    }
                }

                break;

            case System.Windows.DataTrigger dt:
                dto.Conditions.Add(new TriggerConditionDto
                {
                    Property = dt.Binding is System.Windows.Data.Binding b
                        ? b.Path?.Path ?? string.Empty
                        : dt.Binding?.ToString() ?? string.Empty,
                    // DataTrigger condition values are binding-path values — no DP name to redact against; leave as-is.
                    Value = dt.Value?.ToString() ?? string.Empty,
                });
                foreach (System.Windows.SetterBase sb in dt.Setters)
                {
                    if (sb is System.Windows.Setter s)
                    {
                        dto.Setters.Add(new TriggerSetterDto
                        {
                            Property = $"{s.Property?.OwnerType?.Name}.{s.Property?.Name}",
                            Value = enableRedaction
                                ? RedactionFilter.Redact(s.Property?.Name ?? string.Empty, null, s.Value)
                                : s.Value?.ToString() ?? string.Empty,
                        });
                    }
                }

                break;

            case System.Windows.MultiTrigger mt:
                foreach (System.Windows.Condition cond in mt.Conditions)
                {
                    dto.Conditions.Add(new TriggerConditionDto
                    {
                        Property = $"{cond.Property?.OwnerType?.Name}.{cond.Property?.Name}",
                        Value = enableRedaction
                            ? RedactionFilter.Redact(cond.Property?.Name ?? string.Empty, null, cond.Value)
                            : cond.Value?.ToString() ?? string.Empty,
                    });
                }

                foreach (System.Windows.SetterBase sb in mt.Setters)
                {
                    if (sb is System.Windows.Setter s)
                    {
                        dto.Setters.Add(new TriggerSetterDto
                        {
                            Property = $"{s.Property?.OwnerType?.Name}.{s.Property?.Name}",
                            Value = enableRedaction
                                ? RedactionFilter.Redact(s.Property?.Name ?? string.Empty, null, s.Value)
                                : s.Value?.ToString() ?? string.Empty,
                        });
                    }
                }

                break;

            case System.Windows.MultiDataTrigger mdt:
                foreach (System.Windows.Condition cond in mdt.Conditions)
                {
                    dto.Conditions.Add(new TriggerConditionDto
                    {
                        Property = cond.Binding is System.Windows.Data.Binding bCond
                            ? bCond.Path?.Path ?? string.Empty
                            : cond.Binding?.ToString() ?? string.Empty,
                        // MultiDataTrigger condition values are binding-path values — no DP name to redact against; leave as-is.
                        Value = cond.Value?.ToString() ?? string.Empty,
                    });
                }

                foreach (System.Windows.SetterBase sb in mdt.Setters)
                {
                    if (sb is System.Windows.Setter s)
                    {
                        dto.Setters.Add(new TriggerSetterDto
                        {
                            Property = $"{s.Property?.OwnerType?.Name}.{s.Property?.Name}",
                            Value = enableRedaction
                                ? RedactionFilter.Redact(s.Property?.Name ?? string.Empty, null, s.Value)
                                : s.Value?.ToString() ?? string.Empty,
                        });
                    }
                }

                break;

            case System.Windows.EventTrigger et:
                // EventTrigger has no DP-keyed value to redact — SourceName is an element name reference.
                dto.Conditions.Add(new TriggerConditionDto
                {
                    Property = et.RoutedEvent?.Name ?? string.Empty,
                    Value = et.SourceName ?? string.Empty,
                });
                break;
        }

        return dto;
    }

    /// <summary>
    /// Reads behaviors via Interaction.GetBehaviors() reflection for one assembly-qualified type name.
    /// If the assembly is not loaded, returns without error.
    /// Applies redaction to behavior property values when enableRedaction is true.
    /// </summary>
    private static void CollectBehaviorsFromInteraction(DependencyObject depObj, string assemblyQualifiedName, List<BehaviorDto> result, bool enableRedaction)
    {
        var interactionType = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (interactionType is null)
        {
            return;
        }

        var getBehaviorsMethod = interactionType.GetMethod("GetBehaviors", BindingFlags.Static | BindingFlags.Public);
        if (getBehaviorsMethod is null)
        {
            return;
        }

        var behaviors = getBehaviorsMethod.Invoke(null, new object[] { depObj }) as IEnumerable;
        if (behaviors is null)
        {
            return;
        }

        foreach (var behavior in behaviors)
        {
            if (behavior is null)
            {
                continue;
            }

            var behaviorType = behavior.GetType();
            var dto = new BehaviorDto
            {
                TypeName = behaviorType.FullName ?? behaviorType.Name,
                AssemblyName = behaviorType.Assembly.GetName().Name ?? string.Empty,
                Properties = new List<NameValuePairDto>(),
            };

            // Read public instance properties via reflection (NOT TypeDescriptor per security rules).
            foreach (var prop in behaviorType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                try
                {
                    var value = prop.GetValue(behavior);
                    dto.Properties.Add(new NameValuePairDto
                    {
                        Name = prop.Name,
                        Value = enableRedaction
                            ? RedactionFilter.Redact(prop.Name, prop.PropertyType, value)
                            : value?.ToString() ?? string.Empty,
                    });
                }
                catch
                {
                    // Best-effort; skip unreadable properties.
                }
            }

            result.Add(dto);
        }
    }
}
