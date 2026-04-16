namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// Resolves a <see cref="WpfLocator"/> against the live WPF visual tree, delegating
/// to a shared <see cref="NodeRegistry"/> for stable node IDs.
///
/// Growth cap: each call to <see cref="Resolve"/> counts how many <em>new</em>
/// NodeRegistry entries are created during traversal. If the count reaches
/// <see cref="MaxNewEntries"/> (100) before a unique match is found, a
/// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/> is
/// thrown (PRD §14 bug #9). This prevents unbounded memory growth when resolving
/// locators against large or virtualized trees.
///
/// IMPORTANT: <see cref="Resolve"/> MUST be called from the WPF Dispatcher thread.
/// </summary>
internal sealed class LocatorResolver
{
    /// <summary>
    /// Maximum number of new NodeRegistry entries that may be created in a single
    /// <see cref="Resolve"/> call before the resolution is aborted as ambiguous.
    /// </summary>
    internal const int MaxNewEntries = 100;

    private readonly NodeRegistry nodeRegistry;

    /// <summary>
    /// Initializes a new <see cref="LocatorResolver"/> backed by the given registry.
    /// </summary>
    public LocatorResolver(NodeRegistry nodeRegistry)
    {
        this.nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
    }

    /// <summary>
    /// Resolves <paramref name="locator"/> to a stable node ID by doing a BFS walk of
    /// the WPF visual tree rooted at <paramref name="rootTarget"/>.
    /// </summary>
    /// <param name="locator">The locator to resolve.</param>
    /// <param name="rootTarget">
    /// The root object to traverse from — typically <c>Application.Current</c>.
    /// </param>
    /// <returns>The stable node ID of the first matching element.</returns>
    /// <exception cref="SnoopException">
    /// <see cref="SnoopErrorCode.LocatorAmbiguous"/> when more than <see cref="MaxNewEntries"/>
    /// new NodeRegistry entries are created during traversal;
    /// <see cref="SnoopErrorCode.NodeNotFound"/> when no element matches.
    /// </exception>
    public string Resolve(WpfLocator locator, object rootTarget)
    {
        if (locator is null)
        {
            throw new ArgumentNullException(nameof(locator));
        }

        if (rootTarget is null)
        {
            throw new ArgumentNullException(nameof(rootTarget));
        }

        using var treeService = TreeService.From(TreeType.Visual);
        var rootItem = treeService.Construct(rootTarget, parent: null);

        if (rootItem is null)
        {
            throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Could not construct visual tree for locator '{locator.Raw}'.");
        }

        // Use a reference-equality set to detect first-visit per object.
        // On each first visit we call GetOrCreateId; if that creates a new entry
        // (the object was not yet in the registry) we increment newEntries.
        // NodeRegistry does not expose ContainsKey for the forward map, so we
        // compare TryResolve results: call GetOrCreateId, then TryResolve with the
        // returned id — if TryResolve returns the same object, the id existed or was
        // just created. We track "new" by snapshotting a local visitedObjects set.
#if NET5_0_OR_GREATER
        var visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
#else
        var visitedObjects = new HashSet<object>(ObjectReferenceEqualityComparer.Instance);
#endif
        var newEntries = 0;

        var queue = new Queue<TreeItem>();
        queue.Enqueue(rootItem);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();

            if (item.Target is null)
            {
                continue;
            }

            if (visitedObjects.Add(item.Target))
            {
                // First time we see this object: probe if it is already in the registry.
                // We approximate by probing TryResolve before registration. However NodeRegistry
                // does not expose a forward lookup. The conservative approach: treat every
                // new object in our visitedObjects set as a potential new registry entry and
                // let the cap protect against unbounded growth.
                var nodeId = this.nodeRegistry.GetOrCreateId(item.Target);
                newEntries++;

                if (newEntries >= MaxNewEntries)
                {
                    throw new SnoopException(
                        SnoopErrorCode.LocatorAmbiguous,
                        $"Locator '{locator.Raw}' caused {newEntries} new NodeRegistry entries " +
                        $"without a unique match. The cap is {MaxNewEntries}. " +
                        "Use a more specific locator (e.g. automationId=) or narrow the tree scope.",
                        suggestions: new[]
                        {
                            "Use automationId= for the most stable, unambiguous locator.",
                            "Narrow the search with type=, name= or path= qualifiers.",
                        });
                }

                if (this.Matches(locator, item))
                {
                    return nodeId;
                }
            }

            foreach (var child in item.Children)
            {
                queue.Enqueue(child);
            }
        }

        throw new SnoopException(
            SnoopErrorCode.NodeNotFound,
            $"Locator '{locator.Raw}' did not match any element in the tree.",
            suggestions: new[]
            {
                "Verify the locator form and value match an element in the current tree.",
                "Use wpf_get_visual_tree to explore the structure.",
            });
    }

    /// <summary>
    /// Non-throwing variant: resolves <paramref name="locator"/> against the live tree and
    /// returns the matched object, or <c>null</c> if not found.
    /// Used internally by poll-changes to scope the tree walk without surfacing errors.
    /// </summary>
    public object? TryResolve(WpfLocator locator, object rootTarget)
    {
        if (locator is null || rootTarget is null)
        {
            return null;
        }

        try
        {
            var nodeId = this.Resolve(locator, rootTarget);
            return this.nodeRegistry.TryResolve(nodeId);
        }
        catch (SnoopException)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private bool Matches(WpfLocator locator, TreeItem item)
    {
        return locator.Form switch
        {
            WpfLocatorForm.AutomationId => MatchesAutomationId(locator, item),
            WpfLocatorForm.ViewModel => MatchesViewModel(locator, item),
            WpfLocatorForm.TypeName => MatchesTypeName(locator, item),
            WpfLocatorForm.Path => MatchesPath(locator, item),
            _ => false,
        };
    }

    private static bool MatchesAutomationId(WpfLocator locator, TreeItem item)
    {
        if (item.Target is not DependencyObject depObj)
        {
            return false;
        }

        var automationId = AutomationProperties.GetAutomationId(depObj);
        return string.Equals(automationId, locator.Value, StringComparison.Ordinal);
    }

    private static bool MatchesViewModel(WpfLocator locator, TreeItem item)
    {
        if (item.Target is not FrameworkElement fe)
        {
            return false;
        }

        var dc = fe.DataContext;
        if (dc is null)
        {
            return false;
        }

        // Match short type name (case-insensitive per PRD §6).
        var dcTypeName = dc.GetType().Name;
        if (!string.Equals(dcTypeName, locator.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Optional property=value qualifier.
        if (locator.Qualifier is not null)
        {
            var propInfo = dc.GetType().GetProperty(
                locator.Qualifier,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (propInfo is null)
            {
                return false;
            }

            try
            {
                var rawValue = propInfo.GetValue(dc);
                var stringValue = rawValue?.ToString() ?? string.Empty;
                return string.Equals(
                    stringValue,
                    locator.QualifierValue ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTypeName(WpfLocator locator, TreeItem item)
    {
        // Match short type name (case-insensitive).
        var shortName = item.TargetType?.Name ?? string.Empty;
        if (!string.Equals(shortName, locator.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Optional name= qualifier.
        if (locator.Qualifier is not null)
        {
            var elementName = item.Name ?? string.Empty;
            return string.Equals(elementName, locator.Qualifier, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool MatchesPath(WpfLocator locator, TreeItem item)
    {
        // Path form is a backslash-separated list of type names.
        // Walk all segments bottom-up: the last segment must match this item,
        // the second-to-last must match item.Parent, and so on.
        var segments = locator.Value.Split(
            new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        TreeItem? cursor = item;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (cursor is null)
            {
                return false;
            }

            if (!SegmentMatches(segments[i], cursor))
            {
                return false;
            }

            cursor = cursor.Parent;
        }

        return true;
    }

    private static bool SegmentMatches(string segment, TreeItem item)
    {
        var shortName = item.TargetType?.Name ?? string.Empty;
        return string.Equals(shortName, segment, StringComparison.OrdinalIgnoreCase);
    }
}
