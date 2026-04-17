// SnoopInspector.Tree.cs
// FX6-A7: Tree navigation methods.

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
    public Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var dispatcherInfo = new DispatcherInfoDto
            {
                Id = 0,
                ThreadId = this.dispatcher.Thread.ManagedThreadId,
                WindowNodeIds = new List<string>(),
            };

            // Enumerate windows, register them, and build inline window summaries.
            var app = Application.Current;
            var windowSummaries = new List<WindowSummaryDto>();
            if (app is not null)
            {
                foreach (Window w in app.Windows)
                {
                    if (w is not null)
                    {
                        var nodeId = this.nodeRegistry.GetOrCreateId(w);
                        dispatcherInfo.WindowNodeIds.Add(nodeId);

                        if (w.IsVisible)
                        {
                            var typeName = w.GetType().Name;
                            windowSummaries.Add(new WindowSummaryDto
                            {
                                NodeId = nodeId,
                                // ADV-PI: Window.Title is app-controlled; guard trust-boundary parity with DisplayName sites.
                                Title = PromptInjectionGuard.Quote(w.Title ?? string.Empty),
                                Width = w.ActualWidth,
                                Height = w.ActualHeight,
                                Locator = $"$type:{typeName}",
                            });
                        }
                    }
                }
            }

            return new SessionInfoDto
            {
                ProcessName = proc.ProcessName,
                Pid = proc.Id,
                DotnetVersion = Environment.Version.ToString(),
                MutationEnabled = this.options.EnableMutation,
                Dispatchers = new List<DispatcherInfoDto> { dispatcherInfo },
                Capabilities = new List<string>
                {
                    "tree",
                    "properties",
                    "bindings",
                    "diagnostics",
                },
                Windows = windowSummaries,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var result = new List<WindowDto>();

            var app = Application.Current;
            if (app is null)
            {
                return result;
            }

            // Main window first, then remaining windows ordered by title.
            var windows = new List<Window>();
            foreach (Window w in app.Windows)
            {
                if (w is not null)
                {
                    windows.Add(w);
                }
            }

            // Sort: main window first, then by title.
            windows.Sort((a, b) =>
            {
                var aIsMain = ReferenceEquals(a, app.MainWindow);
                var bIsMain = ReferenceEquals(b, app.MainWindow);

                if (aIsMain && !bIsMain)
                {
                    return -1;
                }

                if (!aIsMain && bIsMain)
                {
                    return 1;
                }

                return string.Compare(a.Title ?? string.Empty, b.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var w in windows)
            {
                if (!includeHidden && !w.IsVisible)
                {
                    continue;
                }

                result.Add(new WindowDto
                {
                    NodeId = this.nodeRegistry.GetOrCreateId(w),
                    // ADV-PI: Window.Title is app-controlled; guard trust-boundary parity with DisplayName sites.
                    Title = PromptInjectionGuard.Quote(w.Title ?? string.Empty),
                    TypeName = w.GetType().Name,
                    Width = w.ActualWidth,
                    Height = w.ActualHeight,
                    DispatcherId = 0,
                });
            }

            return result;
        }, ct);
    }

    /// <inheritdoc/>
    public Task<VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveRootTarget(rootNodeId);

            // Clamp includeProperties to max 10.
            var propNames = includeProperties?.Take(10).ToList();

            // Clamp maxDepth at engine layer (tool schema advertises max: 10).
            int effectiveMaxDepth = Math.Min(Math.Max(maxDepth, 1), InputConstants.MaxTreeDepth);

            var treeTypeEnum = ParseTreeType(treeType);
            using var treeService = TreeService.From(treeTypeEnum);

            var rootItem = treeService.Construct(target, parent: null);
            if (rootItem is null)
            {
                throw new SnoopException(
                    SnoopErrorCode.SessionNotFound,
                    "TreeService.Construct returned null — application may not be initialized.",
                    targetId: rootNodeId,
                    suggestions: new[] { SnoopSuggestions.SessionNotFound });
            }

            var nodeCount = 0;
            var truncated = false;
            const int maxNodes = 500;

            var rootDto = this.BuildNodeDtoRecursive(rootItem, 0, effectiveMaxDepth, propNames, ref nodeCount, maxNodes, ref truncated);

            return new VisualTreeResultDto
            {
                Root = rootDto,
                Truncated = truncated,
                ReturnedNodeCount = nodeCount,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<CursorPage<NodeDto>> GetChildrenAsync(
        string? nodeId,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        int effectiveTake = Math.Min(Math.Max(take, 1), InputConstants.MaxPageSize);

        // If we have an existing cursor, serve from snapshot (resolve IDs on Dispatcher).
        if (cursor is not null)
        {
            var existingPage = this.cursorManager.GetPage(cursor, effectiveTake, nodeId);

            if (existingPage.Items.Count > 0 || (!existingPage.HasMore && !existingPage.Stale))
            {
                return this.RunOnDispatcherAsync(() =>
                {
                    var dtos = new List<NodeDto>(existingPage.Items.Count);
                    foreach (var id in existingPage.Items)
                    {
                        var obj = this.nodeRegistry.TryResolve(id);
                        if (obj is null)
                        {
                            continue;
                        }

                        dtos.Add(new NodeDto
                        {
                            NodeId = id,
                            TypeName = obj.GetType().Name,
                            // ADV-PI: obj.ToString() is user-controlled (target-app WPF objects).
                            // Wrap in trust-boundary markers so the LLM cannot be steered by
                            // injected prompt text in element display names (5x5 #6, ADV-C1/C2).
                            DisplayName = PromptInjectionGuard.Quote(obj.ToString()),
                        });
                    }

                    return new CursorPage<NodeDto>
                    {
                        Items = dtos,
                        NextCursor = existingPage.NextCursor,
                        TotalCount = existingPage.TotalCount,
                        HasMore = existingPage.HasMore,
                        Stale = existingPage.Stale,
                    };
                }, ct);
            }
        }

        return this.RunOnDispatcherAsync(() =>
        {
            if (nodeId is null)
            {
                // Root: use application windows. Main window first.
                var app = Application.Current;
                if (app is null)
                {
                    throw new SnoopException(
                        SnoopErrorCode.SessionNotFound,
                        "Application.Current is null.",
                        suggestions: new[] { SnoopSuggestions.SessionNotFound });
                }

                var windows = new List<Window>();
                foreach (Window w in app.Windows)
                {
                    if (w is not null)
                    {
                        windows.Add(w);
                    }
                }

                // Main window first, then by title.
                windows.Sort((a, b) =>
                {
                    var aIsMain = ReferenceEquals(a, app.MainWindow);
                    var bIsMain = ReferenceEquals(b, app.MainWindow);

                    if (aIsMain && !bIsMain)
                    {
                        return -1;
                    }

                    if (!aIsMain && bIsMain)
                    {
                        return 1;
                    }

                    return string.Compare(a.Title ?? string.Empty, b.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                });

                var allIds = windows.Select(w => this.nodeRegistry.GetOrCreateId(w)).ToList();
                var newCursor = this.cursorManager.CreateCursor(allIds, nodeId: "ROOT");
                var firstPage = this.cursorManager.GetPage(newCursor, effectiveTake, nodeId: "ROOT");

                var windowDtos = new List<NodeDto>(firstPage.Items.Count);
                foreach (var id in firstPage.Items)
                {
                    var obj = this.nodeRegistry.TryResolve(id);
                    if (obj is null)
                    {
                        continue;
                    }

                    windowDtos.Add(new NodeDto
                    {
                        NodeId = id,
                        TypeName = obj.GetType().Name,
                        Name = obj is Window win ? win.Title ?? string.Empty : string.Empty,
                        // ADV-PI: same trust boundary as visual-tree nodes above.
                        DisplayName = PromptInjectionGuard.Quote(obj.ToString()),
                    });
                }

                return new CursorPage<NodeDto>
                {
                    Items = windowDtos,
                    NextCursor = firstPage.NextCursor,
                    TotalCount = firstPage.TotalCount,
                    HasMore = firstPage.HasMore,
                    Stale = firstPage.Stale,
                };
            }

            // Resolve specific node.
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var treeTypeEnum = ParseTreeType(treeType);
            using var treeService = TreeService.From(treeTypeEnum);

            var parentItem = treeService.Construct(target, parent: null);

            // Register all children and build snapshot.
            var childIds = new List<string>(parentItem.Children.Count);
            foreach (var child in parentItem.Children)
            {
                childIds.Add(this.nodeRegistry.GetOrCreateId(child.Target));
            }

            // Build the first page.  FX6-A2: bind cursor to parent nodeId.
            var snapshotCursor = this.cursorManager.CreateCursor(childIds, nodeId: nodeId);
            var childPage = this.cursorManager.GetPage(snapshotCursor, effectiveTake, nodeId: nodeId);

            // Resolve child items to DTOs.
            var childItemMap = new Dictionary<string, TreeItem>();
            foreach (var child in parentItem.Children)
            {
                var childId = this.nodeRegistry.GetOrCreateId(child.Target);
                childItemMap[childId] = child;
            }

            var childDtos = new List<NodeDto>(childPage.Items.Count);
            foreach (var id in childPage.Items)
            {
                if (childItemMap.TryGetValue(id, out var childItem))
                {
                    childDtos.Add(DtoProjection.ToNodeDto(childItem, this.nodeRegistry));
                }
            }

            return new CursorPage<NodeDto>
            {
                Items = childDtos,
                NextCursor = childPage.NextCursor,
                TotalCount = childPage.TotalCount,
                HasMore = childPage.HasMore,
                Stale = childPage.Stale,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var typeName = target.GetType().FullName ?? target.GetType().Name;
            var typeShortName = target.GetType().Name;
            var name = string.Empty;

            if (target is FrameworkElement fe)
            {
                name = fe.Name ?? string.Empty;
            }
            else if (target is FrameworkContentElement fce)
            {
                name = fce.Name ?? string.Empty;
            }

            var displayName = string.IsNullOrEmpty(name) ? typeShortName : $"{typeShortName} ({name})";
            var path = new List<string> { typeShortName };

            var isVisible = false;
            var actualWidth = 0.0;
            var actualHeight = 0.0;
            var dataContextType = string.Empty;

            if (target is FrameworkElement feTarget)
            {
                isVisible = feTarget.IsVisible;
                actualWidth = feTarget.ActualWidth;
                actualHeight = feTarget.ActualHeight;

                if (feTarget.DataContext is { } dc)
                {
                    dataContextType = dc.GetType().FullName ?? dc.GetType().Name;
                }
            }

            // Count children via visual tree.
            using var treeService = TreeService.From(TreeType.Visual);
            var treeItem = treeService.Construct(target, parent: null);
            var childCount = treeItem.Children.Count;

            var hasBindingErrors = treeItem.HasBindingError;
            var bindingErrorCount = hasBindingErrors ? 1 : 0;

            // Resolve parent node ID — no longer hard-coded empty (PRD §14 debt item).
            var parentNodeId = string.Empty;
            DependencyObject? parentDepObj = null;
            if (target is Visual visualTarget)
            {
                parentDepObj = VisualTreeHelper.GetParent(visualTarget);
            }

            if (parentDepObj is null)
            {
                if (target is FrameworkElement feParentTarget)
                {
                    parentDepObj = feParentTarget.Parent as DependencyObject;
                }
                else if (target is FrameworkContentElement fceParentTarget)
                {
                    parentDepObj = fceParentTarget.Parent as DependencyObject;
                }
            }

            if (parentDepObj is not null)
            {
                parentNodeId = this.nodeRegistry.GetOrCreateId(parentDepObj);
            }

            return new InspectElementDto
            {
                NodeId = nodeId,
                TypeName = typeName,
                Name = name,
                DisplayName = displayName,
                Path = path,
                ParentNodeId = parentNodeId,
                ChildCount = childCount,
                Depth = 0,
                DispatcherId = 0,
                IsVisible = isVisible,
                ActualWidth = actualWidth,
                ActualHeight = actualHeight,
                DataContextType = dataContextType,
                HasBindingErrors = hasBindingErrors,
                BindingErrorCount = bindingErrorCount,
                TriggerCount = null,
                BehaviorCount = null,
            };
        }, ct);
    }

    public Task<List<AncestorDto>> GetAncestorsAsync(string nodeId, int? maxLevels, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var limit = maxLevels ?? 50;
            if (limit <= 0)
            {
                limit = 50;
            }

            var ancestors = new List<AncestorDto>();

            // Walk up the visual tree using VisualTreeHelper.GetParent().
            // For non-Visual objects, try logical tree via FrameworkElement.Parent.
            DependencyObject? current = target as DependencyObject;

            while (current is not null && ancestors.Count < limit)
            {
                DependencyObject? parent = null;

                // Visual tree first (most reliable for layout elements).
                if (current is Visual)
                {
                    parent = VisualTreeHelper.GetParent(current);
                }

                // Fall back to logical tree (covers ContentElement, FrameworkContentElement).
                if (parent is null)
                {
                    if (current is FrameworkElement fe)
                    {
                        parent = fe.Parent as DependencyObject;
                    }
                    else if (current is FrameworkContentElement fce)
                    {
                        parent = fce.Parent as DependencyObject;
                    }
                }

                if (parent is null)
                {
                    break;
                }

                // Register ancestor in registry for stable IDs.
                var ancestorId = this.nodeRegistry.GetOrCreateId(parent);

                var typeName = parent.GetType().FullName ?? parent.GetType().Name;
                var shortName = parent.GetType().Name;
                var name = string.Empty;
                var dataContextType = string.Empty;

                if (parent is FrameworkElement ancestorFe)
                {
                    name = ancestorFe.Name ?? string.Empty;
                    if (ancestorFe.DataContext is { } dc)
                    {
                        dataContextType = dc.GetType().FullName ?? dc.GetType().Name;
                    }
                }
                else if (parent is FrameworkContentElement ancestorFce)
                {
                    name = ancestorFce.Name ?? string.Empty;
                }

                ancestors.Add(new AncestorDto
                {
                    NodeId = ancestorId,
                    TypeName = typeName,
                    Name = name,
                    DataContextType = dataContextType,
                });

                current = parent;
            }

            return ancestors;
        }, ct);
    }

    /// <inheritdoc/>
    public Task<FindElementResultDto> FindElementsAsync(
        string? typeName,
        string? name,
        string? rootNodeId,
        List<PropertyConditionDto>? conditions,
        string treeType,
        int maxResults,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            // Cap maxResults at 100.
            var cap = Math.Min(maxResults <= 0 ? 100 : maxResults, 100);

            var root = this.ResolveRootTarget(rootNodeId);

            var treeTypeEnum = ParseTreeType(treeType);
            using var treeService = TreeService.From(treeTypeEnum);
            var rootItem = treeService.Construct(root, parent: null);

            if (rootItem is null)
            {
                return new FindElementResultDto
                {
                    Results = new List<FindElementHitDto>(),
                    TotalScanned = 0,
                    Truncated = false,
                };
            }

            var results = new List<FindElementHitDto>();
            var totalScanned = 0;
            var truncated = false;

            // BFS traversal of the tree.
            // Use parallel queues to avoid ValueTuple (not available on net462 without NuGet).
            var itemQueue = new Queue<TreeItem>();
            var pathQueue = new Queue<List<string>>();

            itemQueue.Enqueue(rootItem);
            pathQueue.Enqueue(new List<string>());

            while (itemQueue.Count > 0 && !truncated)
            {
                var current = itemQueue.Dequeue();
                var pathSoFar = pathQueue.Dequeue();
                totalScanned++;

                if (this.MatchesFilter(current, typeName, name, conditions))
                {
                    if (results.Count >= cap)
                    {
                        truncated = true;
                        break;
                    }

                    var nodeDto = DtoProjection.ToNodeDto(current, this.nodeRegistry);
                    results.Add(new FindElementHitDto
                    {
                        Node = nodeDto,
                        Path = new List<string>(pathSoFar),
                        HasCommandBinding = HasCommandBinding(current),
                    });
                }

                // Enqueue children with updated path.
                var childPath = new List<string>(pathSoFar)
                {
                    current.TargetType?.Name ?? current.Target?.GetType().Name ?? string.Empty,
                };

                foreach (var child in current.Children)
                {
                    itemQueue.Enqueue(child);
                    pathQueue.Enqueue(childPath);
                }
            }

            return new FindElementResultDto
            {
                Results = results,
                TotalScanned = totalScanned,
                Truncated = truncated,
            };
        }, ct);
    }

    /// <summary>
    /// Returns true if the element has a Command dependency property with a non-null value
    /// or an active binding expression on <see cref="ButtonBase.CommandProperty"/>.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private static bool HasCommandBinding(TreeItem item)
    {
        if (item.Target is not DependencyObject depObj)
        {
            return false;
        }

        // A set local value (e.g. Command="{Binding ...}" after binding resolves, or literal).
        var commandValue = depObj.GetValue(ButtonBase.CommandProperty);
        if (commandValue != null)
        {
            return true;
        }

        // A binding expression that hasn't resolved yet (e.g. in design mode or before DataContext).
        var bindingExpr = BindingOperations.GetBindingExpression(depObj, ButtonBase.CommandProperty);
        return bindingExpr != null;
    }

    /// <summary>
    /// Returns true if the given TreeItem matches the search filter criteria.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private bool MatchesFilter(
        TreeItem item,
        string? typeNameFilter,
        string? nameFilter,
        List<PropertyConditionDto>? conditions)
    {
        // Type name filter: short name ("Button") or full name, case-insensitive substring match.
        if (!string.IsNullOrEmpty(typeNameFilter))
        {
            var shortName = item.TargetType?.Name ?? string.Empty;
            var fullName = item.TargetType?.FullName ?? string.Empty;

            var matchesType =
                shortName.IndexOf(typeNameFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf(typeNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!matchesType)
            {
                return false;
            }
        }

        // Name filter: match x:Name / Name property.
        if (!string.IsNullOrEmpty(nameFilter))
        {
            var elementName = item.Name ?? string.Empty;
            if (elementName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        // Property conditions: check each condition against element's dependency properties.
        if (conditions is { Count: > 0 } && item.Target is DependencyObject depObj)
        {
            foreach (var condition in conditions)
            {
                if (string.IsNullOrEmpty(condition.Property))
                {
                    continue;
                }

                if (!this.EvaluatePropertyCondition(depObj, condition))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single property condition against a DependencyObject.
    /// Uses reflection on public instance properties (NOT TypeDescriptor) per security rules.
    /// Called only on the Dispatcher thread.
    /// </summary>
    private bool EvaluatePropertyCondition(DependencyObject depObj, PropertyConditionDto condition)
    {
        try
        {
            // Look up property via reflection — public instance properties only.
            // Per global security rules: use GetProperties(BindingFlags) NOT TypeDescriptor.GetProperties().
            var propInfo = depObj.GetType().GetProperty(
                condition.Property,
                BindingFlags.Public | BindingFlags.Instance);

            if (propInfo is null)
            {
                // Unknown property — condition cannot match.
                return false;
            }

            // Guard: do not expose sensitive property values via comparison oracle.
            // A caller could binary-search a password by issuing repeated FindElements calls.
            if (this.options.EnableRedaction && RedactionFilter.IsRedacted(condition.Property, propInfo.PropertyType))
            {
                return false;
            }

            var rawValue = propInfo.GetValue(depObj);
            var stringValue = rawValue?.ToString() ?? string.Empty;

            var op = condition.Operator ?? "Equals";

            if (string.Equals(op, "Equals", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(stringValue, condition.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(op, "Contains", StringComparison.OrdinalIgnoreCase))
            {
                return stringValue.IndexOf(condition.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // Unknown operator — condition cannot match.
            return false;
        }
        catch
        {
            // Property access can throw (e.g. for elements in invalid state).
            return false;
        }
    }

    /// <inheritdoc/>

    public async Task<VisualTreeResultDto> GetVisualTreeAsync(
        WpfLocator locator,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetVisualTreeAsync(nodeId, maxDepth, treeType, includeProperties, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CursorPage<NodeDto>> GetChildrenAsync(
        WpfLocator locator,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetChildrenAsync(nodeId, treeType, cursor, take, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<AncestorDto>> GetAncestorsAsync(
        WpfLocator locator,
        int? maxLevels,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetAncestorsAsync(nodeId, maxLevels, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<InspectElementDto> InspectElementAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.InspectElementAsync(nodeId, ct).ConfigureAwait(false);
    }
}
