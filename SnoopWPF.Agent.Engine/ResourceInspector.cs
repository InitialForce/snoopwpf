namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// Walks the element tree upward, collecting ResourceDictionary entries with origin labels.
/// Effective (closest scope) resources come first; shadowed entries are included with their origin.
/// IMPORTANT: Must be called from within a Dispatcher.Invoke block — accesses live WPF objects.
/// </summary>
public static class ResourceInspector
{
    /// <summary>
    /// Collects all resource entries visible from <paramref name="element"/>, walking up the tree.
    /// </summary>
    /// <param name="element">The starting WPF element (DependencyObject).</param>
    /// <param name="resourceKey">Optional key filter — if non-null, only matching entries are returned.</param>
    /// <param name="enableRedaction">Whether to redact sensitive resource values.</param>
    /// <returns>Ordered list of resource DTOs (closest scope first).</returns>
    public static List<ResourceDto> GetResources(
        DependencyObject element,
        string? resourceKey,
        bool enableRedaction = true)
    {
        var result = new List<ResourceDto>();
        var seenKeys = new HashSet<object>();

        // Walk up the visual/logical tree collecting resources from each scope.
        var current = element;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.Resources is { Count: > 0 } feResources)
            {
                CollectFromDictionary(feResources, "Element", current.GetType().Name, resourceKey, enableRedaction, seenKeys, result);
            }
            else if (current is FrameworkContentElement fce && fce.Resources is { Count: > 0 } fceResources)
            {
                CollectFromDictionary(fceResources, "Element", current.GetType().Name, resourceKey, enableRedaction, seenKeys, result);
            }

            // Walk up: try visual parent first, fall back to logical parent.
            DependencyObject? parent = null;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(current);
            }

            if (parent is null)
            {
                parent = LogicalTreeHelper.GetParent(current);
            }

            current = parent;
        }

        // Window-level resources (if not already covered by tree walk).
        if (element is Visual visual)
        {
            var window = Window.GetWindow(visual);
            if (window is not null && window.Resources is { Count: > 0 } windowResources)
            {
                CollectFromDictionary(windowResources, "Window", window.Title ?? window.GetType().Name, resourceKey, enableRedaction, seenKeys, result);
            }
        }

        // Application-level resources.
        if (Application.Current?.Resources is { Count: > 0 } appResources)
        {
            CollectFromDictionary(appResources, "Application", "Application", resourceKey, enableRedaction, seenKeys, result);
        }

        return result;
    }

    private static void CollectFromDictionary(
        ResourceDictionary dictionary,
        string scope,
        string dictionarySource,
        string? keyFilter,
        bool enableRedaction,
        HashSet<object> seenKeys,
        List<ResourceDto> result)
    {
        // Collect from this dictionary first.
        foreach (var key in dictionary.Keys)
        {
            // Apply key filter if specified.
            if (keyFilter is not null
                && !string.Equals(key?.ToString(), keyFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            object? value = null;
            try
            {
                value = dictionary[key];
            }
            catch
            {
                // Skip entries that throw on access.
                continue;
            }

            var keyStr = key?.ToString() ?? string.Empty;
            var valueType = value?.GetType();
            var valueTypeName = valueType?.Name ?? "null";

            var isRedacted = enableRedaction && RedactionFilter.IsRedacted(keyStr, valueType);
            var valueSummary = isRedacted
                ? "[REDACTED]"
                : (value?.ToString() ?? "null");

            // Track seen keys so callers can see shadowing; include all entries with their origin.
            // seenKeys is used to mark whether this is the effective (first seen) or shadowed entry.
            var isEffective = seenKeys.Add(key!);
            var originLabel = isEffective ? scope : $"{scope} (shadowed)";

            result.Add(new ResourceDto
            {
                Key = keyStr,
                ValueTypeName = valueTypeName,
                ValueSummary = valueSummary,
                Origin = originLabel,
                DictionarySource = dictionarySource,
            });
        }

        // Recurse into merged dictionaries (in order, so closest merged = effective).
        foreach (var merged in dictionary.MergedDictionaries)
        {
            CollectFromDictionary(merged, scope, $"{dictionarySource} (merged)", keyFilter, enableRedaction, seenKeys, result);
        }
    }
}
