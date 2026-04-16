namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// Core inspection engine. Implements <see cref="ISnoopInspector"/> by marshaling all WPF operations
/// to the provided Dispatcher. Concurrency is capped at 3 simultaneous Dispatcher invocations.
/// </summary>
public sealed class SnoopInspector : ISnoopInspector, IDisposable
{
    // Acceptance timeout: if the Dispatcher won't even accept work within this window, it's DispatcherBusy.
    private const int DispatcherAcceptanceTimeoutMs = 500;

    private readonly Dispatcher dispatcher;
    private readonly object? rootTarget;
    private readonly SnoopInspectorOptions options;

    private readonly NodeRegistry nodeRegistry;
    private readonly CursorManager cursorManager;

    // Max 3 concurrent Dispatcher operations.
    private readonly SemaphoreSlim concurrencySemaphore = new(3, 3);

    private volatile bool disposed;

    /// <summary>
    /// Initializes a new SnoopInspector.
    /// </summary>
    /// <param name="dispatcher">The WPF Dispatcher for the target application.</param>
    /// <param name="rootTarget">
    /// The root inspection target — typically <c>Application.Current</c> in NuGet mode,
    /// or a specific injection root in injection mode.
    /// </param>
    /// <param name="options">Optional configuration; defaults are applied if null.</param>
    public SnoopInspector(
        Dispatcher dispatcher,
        object? rootTarget = null,
        SnoopInspectorOptions? options = null)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.rootTarget = rootTarget;
        this.options = options ?? new SnoopInspectorOptions();

        this.nodeRegistry = new NodeRegistry();
        this.cursorManager = new CursorManager();
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.nodeRegistry.Dispose();
        this.cursorManager.Dispose();
        this.concurrencySemaphore.Dispose();
    }

    // -------------------------------------------------------------------------
    // ISnoopInspector — implemented methods
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
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

            // Enumerate windows and register them.
            var app = Application.Current;
            if (app is not null)
            {
                foreach (Window w in app.Windows)
                {
                    if (w is not null)
                    {
                        dispatcherInfo.WindowNodeIds.Add(this.nodeRegistry.GetOrCreateId(w));
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
                    Title = w.Title ?? string.Empty,
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

            var rootDto = this.BuildNodeDtoRecursive(rootItem, 0, maxDepth, propNames, ref nodeCount, maxNodes, ref truncated);

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
        // If we have an existing cursor, serve from snapshot (resolve IDs on Dispatcher).
        if (cursor is not null)
        {
            var existingPage = this.cursorManager.GetPage(cursor, take);

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
                            DisplayName = obj.ToString() ?? obj.GetType().Name,
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
                var newCursor = this.cursorManager.CreateCursor(allIds);
                var firstPage = this.cursorManager.GetPage(newCursor, take);

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
                        DisplayName = obj.ToString() ?? obj.GetType().Name,
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

            // Build the first page.
            var snapshotCursor = this.cursorManager.CreateCursor(childIds);
            var childPage = this.cursorManager.GetPage(snapshotCursor, take);

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

            return new InspectElementDto
            {
                NodeId = nodeId,
                TypeName = typeName,
                Name = name,
                DisplayName = displayName,
                Path = path,
                ParentNodeId = string.Empty,
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

    /// <inheritdoc/>
    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            // ONE synchronous block: get properties, read values, project, teardown.
            List<PropertyDto> allDtos;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                allDtos = new List<PropertyDto>(props.Count);

                foreach (var prop in props)
                {
                    // Apply includeDefaults filter before projecting to DTOs (cheaper).
                    // When includeDefaults=false, only include non-default properties.
                    if (!includeDefaults)
                    {
                        if (!prop.IsLocallySet && !prop.IsDatabound && !prop.IsInvalidBinding && !prop.IsExpression)
                        {
                            continue;
                        }
                    }

                    // Apply category filter before projecting to DTOs (cheaper).
                    // "all" or null means no category filter. Uses the PropertyDescriptor Category attribute.
                    if (!string.IsNullOrEmpty(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        var propCategory = prop.Property?.Category ?? string.Empty;
                        if (!string.Equals(propCategory, category, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var dto = DtoProjection.ToPropertyDto(prop, this.options.EnableRedaction);
                    allDtos.Add(dto);
                }
            }
            finally
            {
                // Teardown all PropertyInformation objects; stop any orphaned DispatcherTimers.
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            // Apply optional text filter.
            if (!string.IsNullOrEmpty(filter))
            {
                allDtos = allDtos
                    .Where(p => p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            // Properties are sorted by name by PropertyInformation.GetProperties (calls Sort()).
            // Paginate using index-based cursor snapshot.
            var nodeIds = allDtos.Select((_, i) => i.ToString()).ToList();
            var cursorToken = this.cursorManager.CreateCursor(nodeIds);
            var page = this.cursorManager.GetPage(cursorToken, take);

            var pageItems = new List<PropertyDto>(page.Items.Count);
            foreach (var idxStr in page.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < allDtos.Count)
                {
                    pageItems.Add(allDtos[idx]);
                }
            }

            return new CursorPage<PropertyDto>
            {
                Items = pageItems,
                NextCursor = page.NextCursor,
                TotalCount = page.TotalCount,
                HasMore = page.HasMore,
                Stale = page.Stale,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propertyName, null);

            // ONE synchronous block: get properties, find named one, read binding, teardown.
            BindingInfoDto result;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    result = new BindingInfoDto { HasBinding = false };
                }
                else
                {
                    var dto = DtoProjection.ToBindingInfoDto(match);
                    if (dto is null)
                    {
                        result = new BindingInfoDto { HasBinding = false };
                    }
                    else
                    {
                        if (isRedacted)
                        {
                            dto.Path = "[REDACTED]";
                            dto.ResolvedValue = "[REDACTED]";
                        }

                        result = dto;
                    }
                }
            }
            finally
            {
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            return result;
        }, ct);
    }

    /// <inheritdoc/>
    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            object target;

            if (nodeId is not null)
            {
                target = this.ResolveNodeOrThrow(nodeId);
                this.VerifyElementConnectivity(target, nodeId);
            }
            else
            {
                target = this.GetEffectiveRootTarget();
            }

            using var treeService = TreeService.From(TreeType.Visual);
            treeService.Construct(target, parent: null);

            // Run all diagnostics.
            treeService.DiagnosticContext.AnalyzeTree();

            var diagItems = treeService.DiagnosticContext.DiagnosticItems.ToList();

            // Filter by minLevel if specified.
            if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<DiagnosticLevel>(minLevel, ignoreCase: true, out var minLevelEnum))
            {
                diagItems = diagItems.Where(d => d.Level >= minLevelEnum).ToList();
            }

            // Filter by providers if specified.
            if (providers is { Count: > 0 })
            {
                diagItems = diagItems.Where(d =>
                    providers.Any(p => string.Equals(p, d.DiagnosticProvider.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var dtos = diagItems.Select(d =>
            {
                var nodeIdForItem = d.TreeItem is not null
                    ? this.nodeRegistry.GetOrCreateId(d.TreeItem.Target)
                    : string.Empty;

                return new DiagnosticItemDto
                {
                    Name = d.Name ?? string.Empty,
                    Description = d.Description ?? string.Empty,
                    Area = d.Area.ToString(),
                    Level = d.Level.ToString(),
                    NodeId = nodeIdForItem,
                    NodePath = new List<string>(),
                };
            }).ToList();

            // Paginate.
            var snapIds = dtos.Select((_, i) => i.ToString()).ToList();
            var cursorToken = this.cursorManager.CreateCursor(snapIds);
            var page = this.cursorManager.GetPage(cursorToken, take);

            var pageItems = new List<DiagnosticItemDto>(page.Items.Count);
            foreach (var idxStr in page.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < dtos.Count)
                {
                    pageItems.Add(dtos[idx]);
                }
            }

            return new CursorPage<DiagnosticItemDto>
            {
                Items = pageItems,
                NextCursor = page.NextCursor,
                TotalCount = page.TotalCount,
                HasMore = page.HasMore,
                Stale = page.Stale,
            };
        }, ct);
    }

    // -------------------------------------------------------------------------
    // ISnoopInspector — additional methods
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
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
    public Task<SetPropertyResultDto> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            // Guard: redacted properties cannot be mutated.
            var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propertyName, null);
            if (isRedacted)
            {
                throw new SnoopException(
                    SnoopErrorCode.PropertyRedacted,
                    $"Property '{propertyName}' is redacted and cannot be set.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.PropertyRedacted });
            }

            // Only DependencyObject targets support DP-based property setting.
            if (target is not DependencyObject depObj)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    $"Target '{nodeId}' is not a DependencyObject; property mutation requires a DependencyObject.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }

            // Find the DependencyProperty via PropertyInformation.
            // ONE synchronous block: get properties, find named one, capture type, teardown.
            DependencyProperty? depProp = null;
            Type? propertyType = null;
            string previousValue = string.Empty;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    throw new SnoopException(
                        SnoopErrorCode.PropertyReadOnly,
                        $"Property '{propertyName}' was not found on '{target.GetType().Name}'.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.PropertyReadOnly });
                }

                if (!match.CanEdit)
                {
                    throw new SnoopException(
                        SnoopErrorCode.PropertyReadOnly,
                        $"Property '{propertyName}' is read-only on '{target.GetType().Name}'.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.PropertyReadOnly });
                }

                depProp = match.DependencyProperty;
                propertyType = (Type?)match.PropertyType;

                // Capture previous value (use StringValue — never TypeDescriptor).
                previousValue = match.StringValue ?? string.Empty;
            }
            finally
            {
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            if (depProp is null || propertyType is null)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    $"Property '{propertyName}' on '{target.GetType().Name}' is not a DependencyProperty and cannot be set.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }

            // Convert the value using the hardcoded TypeConverterTable — NEVER TypeDescriptor.GetConverter().
            object convertedValue;
            try
            {
                convertedValue = TypeConverterTable.Convert(propertyType, value);
            }
            catch (NotSupportedException ex)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    ex.Message,
                    ex,
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }
            catch (FormatException ex)
            {
                throw new SnoopException(
                    SnoopErrorCode.TypeConversionFailed,
                    ex.Message,
                    ex,
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.TypeConversionFailed });
            }

            // Apply the value.
            depObj.SetValue(depProp, convertedValue);

            // Log mutation: nodeId + property name only (NOT values — per security rules).
            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetProperty: nodeId={nodeId}, property={propertyName}");

            var newValue = convertedValue.ToString() ?? string.Empty;

            return new SetPropertyResultDto
            {
                Success = true,
                PreviousValue = previousValue,
                NewValue = newValue,
                Error = null,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            object target;

            if (nodeId is not null)
            {
                target = this.ResolveNodeOrThrow(nodeId);
                this.VerifyElementConnectivity(target, nodeId);
            }
            else
            {
                target = this.GetEffectiveRootTarget();
            }

            // ResourceInspector requires a DependencyObject to walk the tree.
            if (target is not DependencyObject depObj)
            {
                // Return empty page — non-DependencyObject roots have no resource dictionaries.
                return new CursorPage<ResourceDto>
                {
                    Items = new List<ResourceDto>(),
                    NextCursor = null,
                    TotalCount = 0,
                    HasMore = false,
                    Stale = false,
                };
            }

            var resources = ResourceInspector.GetResources(depObj, resourceKey, this.options.EnableRedaction);

            // Paginate using index-based cursor snapshot.
            var snapIds = resources.Select((_, i) => i.ToString()).ToList();
            var cursorToken = this.cursorManager.CreateCursor(snapIds);
            var page = this.cursorManager.GetPage(cursorToken, take);

            var pageItems = new List<ResourceDto>(page.Items.Count);
            foreach (var idxStr in page.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < resources.Count)
                {
                    pageItems.Add(resources[idx]);
                }
            }

            return new CursorPage<ResourceDto>
            {
                Items = pageItems,
                NextCursor = page.NextCursor,
                TotalCount = page.TotalCount,
                HasMore = page.HasMore,
                Stale = page.Stale,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            Visual? visual;

            if (nodeId is not null)
            {
                var target = this.ResolveNodeOrThrow(nodeId);
                this.VerifyElementConnectivity(target, nodeId);

                if (target is not Visual v)
                {
                    throw new SnoopException(
                        SnoopErrorCode.ElementNotRenderable,
                        $"Node '{nodeId}' is not a Visual and cannot be captured.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
                }

                visual = v;
            }
            else
            {
                // Default: capture main window.
                var app = Application.Current;
                if (app?.MainWindow is null)
                {
                    throw new SnoopException(
                        SnoopErrorCode.SessionNotFound,
                        "No main window available for screenshot.",
                        suggestions: new[] { SnoopSuggestions.SessionNotFound });
                }

                visual = app.MainWindow;
            }

            // Check the visual has renderable size.
            var size = ScreenshotCapture.GetRenderSize(visual!);
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    $"Element has zero size ({size.Width}x{size.Height}) and cannot be captured.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            var pngBytes = ScreenshotCapture.CaptureAsPng(visual!);

            if (pngBytes is null || pngBytes.Length == 0)
            {
                throw new SnoopException(
                    SnoopErrorCode.ElementNotRenderable,
                    "Screenshot capture returned no data — element may not be visible or connected.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.ElementNotRenderable });
            }

            // Use actual pixel dimensions capped to max.
            var effectiveWidth = (int)Math.Min(size.Width, ScreenshotCapture.MaxDimension);
            var effectiveHeight = (int)Math.Min(size.Height, ScreenshotCapture.MaxDimension);

            return new ScreenshotResultDto
            {
                Metadata = new ScreenshotMetadataDto
                {
                    Width = effectiveWidth,
                    Height = effectiveHeight,
                    NodeId = nodeId ?? string.Empty,
                },
                PngBytes = pngBytes,
            };
        }, ct);
    }

    /// <inheritdoc/>
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

    // -------------------------------------------------------------------------
    // Threading helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs a synchronous action on the WPF Dispatcher with concurrency limiting and timeout.
    /// Two-phase timeout:
    ///   Phase 1 (500ms): Dispatcher acceptance — DispatcherBusy if exceeded.
    ///   Phase 2 (TimeoutMs): Execution — OperationTimedOut if exceeded.
    /// </summary>
    private async Task<T> RunOnDispatcherAsync<T>(Func<T> action, CancellationToken ct)
    {
        this.ThrowIfDisposed();

        // Guard: must not be called from the Dispatcher thread (would deadlock).
        Debug.Assert(
            !this.dispatcher.CheckAccess(),
            "SnoopInspector methods must not be called from the Dispatcher thread.");

        // Guard: Dispatcher must not be shutting down.
        if (this.dispatcher.HasShutdownStarted)
        {
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "The WPF Dispatcher has shut down.",
                suggestions: new[] { SnoopSuggestions.SessionNotFound });
        }

        await this.concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Queue work on the Dispatcher at Send priority.
            var dispatcherOp = this.dispatcher.InvokeAsync(action, DispatcherPriority.Send, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(this.options.TimeoutMs);

            var dispatcherTask = dispatcherOp.Task;

            // Track total budget elapsed so Phase 2 only waits for the *remaining* time.
            var sw = Stopwatch.StartNew();

            // Phase 1: Did the Dispatcher accept (start) the work within the acceptance window?
            var acceptanceDeadline = Task.Delay(DispatcherAcceptanceTimeoutMs, timeoutCts.Token);
            var firstToFinish = await Task.WhenAny(dispatcherTask, acceptanceDeadline).ConfigureAwait(false);

            if (firstToFinish == acceptanceDeadline && !dispatcherTask.IsCompleted)
            {
                dispatcherOp.Abort();
                ct.ThrowIfCancellationRequested();

                throw new SnoopException(
                    SnoopErrorCode.DispatcherBusy,
                    "The WPF Dispatcher did not accept work within the acceptance timeout.",
                    suggestions: new[] { SnoopSuggestions.DispatcherBusy });
            }

            // Phase 2: Work was accepted. Wait for completion within the *remaining* budget.
            var remainingMs = Math.Max(0, this.options.TimeoutMs - (int)sw.ElapsedMilliseconds);
            var completionDeadline = Task.Delay(remainingMs, timeoutCts.Token);
            var finalResult = await Task.WhenAny(dispatcherTask, completionDeadline).ConfigureAwait(false);

            if (finalResult == completionDeadline && !dispatcherTask.IsCompleted)
            {
                dispatcherOp.Abort();
                ct.ThrowIfCancellationRequested();

                throw new SnoopException(
                    SnoopErrorCode.OperationTimedOut,
                    $"The Dispatcher operation did not complete within {this.options.TimeoutMs}ms.",
                    suggestions: new[] { SnoopSuggestions.OperationTimedOut });
            }

            // Propagate exceptions from the Dispatcher lambda.
            return await dispatcherTask.ConfigureAwait(false);
        }
        finally
        {
            this.concurrencySemaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(SnoopInspector));
        }
    }

    private object GetEffectiveRootTarget()
    {
        if (this.rootTarget is not null)
        {
            return this.rootTarget;
        }

        var app = Application.Current;
        if (app is null)
        {
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                "Application.Current is null — application may not be initialized.",
                suggestions: new[] { SnoopSuggestions.SessionNotFound });
        }

        return app;
    }

    private object ResolveRootTarget(string? rootNodeId)
    {
        if (rootNodeId is null)
        {
            return this.GetEffectiveRootTarget();
        }

        return this.ResolveNodeOrThrow(rootNodeId);
    }

    private object ResolveNodeOrThrow(string nodeId)
    {
        // Support path alias: "Window\Grid\Button"
        if (nodeId.IndexOf("\\", StringComparison.Ordinal) >= 0)
        {
            return this.ResolvePathAlias(nodeId);
        }

        var obj = this.nodeRegistry.TryResolve(nodeId);
        if (obj is null)
        {
            throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Node '{nodeId}' was not found in the registry — it may have been garbage collected.",
                targetId: nodeId,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
        }

        return obj;
    }

    private object ResolvePathAlias(string path)
    {
        // Walk the tree from root, matching type names for each segment.
        var segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Empty path alias: '{path}'.",
                targetId: path,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
        }

        using var treeService = TreeService.From(TreeType.Visual);
        var root = treeService.Construct(this.GetEffectiveRootTarget(), parent: null);

        return FindByPathSegments(root, segments, 0)
            ?? throw new SnoopException(
                SnoopErrorCode.NodeNotFound,
                $"Path alias '{path}' did not match any element in the tree.",
                targetId: path,
                suggestions: new[] { SnoopSuggestions.NodeNotFound });
    }

    private static object? FindByPathSegments(TreeItem item, string[] segments, int segmentIndex)
    {
        if (segmentIndex >= segments.Length)
        {
            return item.Target;
        }

        var segment = segments[segmentIndex];

        if (!string.Equals(item.TargetType?.Name, segment, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (segmentIndex == segments.Length - 1)
        {
            return item.Target;
        }

        foreach (var child in item.Children)
        {
            var found = FindByPathSegments(child, segments, segmentIndex + 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void VerifyElementConnectivity(object target, string nodeId)
    {
        if (target is Window window)
        {
            // For Windows, use IsInitialized — PresentationSource can return null for hidden windows.
            if (!window.IsInitialized)
            {
                throw new SnoopException(
                    SnoopErrorCode.NodeNotFound,
                    $"Window node '{nodeId}' is no longer initialized.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.NodeNotFound });
            }
        }

        // Non-Visual and Visual nodes without a presentation source are allowed through
        // (popups, logical-only nodes, etc.). Connectivity failures surface as exceptions
        // during property reads rather than as an explicit gate here.
    }

    private static TreeType ParseTreeType(string treeType)
    {
        if (string.Equals(treeType, "Visual", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Visual;
        }

        if (string.Equals(treeType, "Logical", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Logical;
        }

        if (string.Equals(treeType, "Automation", StringComparison.OrdinalIgnoreCase))
        {
            return TreeType.Automation;
        }

        // Default to Visual if unrecognised.
        return TreeType.Visual;
    }

    private NodeDto BuildNodeDtoRecursive(
        TreeItem item,
        int currentDepth,
        int maxDepth,
        List<string>? includeProperties,
        ref int nodeCount,
        int maxNodes,
        ref bool truncated)
    {
        nodeCount++;

        List<NameValuePairDto>? props = null;

        if (includeProperties is { Count: > 0 } && item.Target is DependencyObject depObj)
        {
            props = this.ReadNamedProperties(depObj, includeProperties);
        }

        var dto = DtoProjection.ToNodeDto(item, this.nodeRegistry, currentDepth);
        dto.Properties = props;

        if (currentDepth >= maxDepth || nodeCount >= maxNodes)
        {
            if (item.Children.Count > 0)
            {
                dto.ChildrenTruncated = true;
                truncated = true;
            }

            return dto;
        }

        if (item.Children.Count > 0)
        {
            dto.Children = new List<NodeDto>(item.Children.Count);

            foreach (var child in item.Children)
            {
                if (nodeCount >= maxNodes)
                {
                    dto.ChildrenTruncated = true;
                    truncated = true;
                    break;
                }

                dto.Children.Add(this.BuildNodeDtoRecursive(
                    child, currentDepth + 1, maxDepth, includeProperties,
                    ref nodeCount, maxNodes, ref truncated));
            }
        }

        return dto;
    }

    private List<NameValuePairDto> ReadNamedProperties(DependencyObject target, List<string> propertyNames)
    {
        var result = new List<NameValuePairDto>(propertyNames.Count);

        var props = PropertyInformation.GetProperties(target);
        try
        {
            foreach (var propName in propertyNames)
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propName, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    var propType = (Type?)match.PropertyType;
                    var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propName, propType);
                    var value = isRedacted ? "[REDACTED]" : (match.StringValue ?? string.Empty);

                    result.Add(new NameValuePairDto { Name = propName, Value = value });
                }
            }
        }
        finally
        {
            foreach (var prop in props)
            {
                prop.Teardown();
                StopChangeTimer(prop);
            }
        }

        return result;
    }

    /// <summary>
    /// Stops the orphaned DispatcherTimer that PropertyInformation.Teardown() does not stop.
    /// Uses reflection to access the private changeTimer field.
    /// </summary>
    private static void StopChangeTimer(PropertyInformation prop)
    {
        try
        {
            var field = typeof(PropertyInformation).GetField(
                "changeTimer",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field?.GetValue(prop) is DispatcherTimer timer)
            {
                timer.Stop();
            }
        }
        catch
        {
            // Best-effort. If reflection fails, the timer will expire naturally.
        }
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
