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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private readonly SessionPolicy? sessionPolicy;

    private readonly NodeRegistry nodeRegistry;
    private readonly CursorManager cursorManager;
    private readonly LocatorResolver locatorResolver;
    private readonly Binding.BindingResolver bindingResolver = new();

    // M2-10: last-poll snapshot for incremental "removed" detection.
    // Stores the liveNodeIds captured at the most-recently returned TreeVersion.
    // Access only on the Dispatcher thread (set inside RunOnDispatcherAsync).
    private long lastPollVersion = -1;
    private System.Collections.Generic.HashSet<string>? lastPollLiveIds;

    // Max 3 concurrent Dispatcher operations.
    private readonly SemaphoreSlim concurrencySemaphore = new(3, 3);

    // FX-C1: CTS cancelled in Dispose() to unblock concurrent WaitAsync calls gracefully.
    private readonly CancellationTokenSource disposeCts = new();

    // M2-11: nested-pump guard — 0 = idle, 1 = pump in progress.
    // Instance-level so it is safe across thread-pool continuation migrations.
    private int pumpInProgress;

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
    /// <param name="sessionPolicy">
    /// Optional session policy. When provided, MF-11 is enforced: if
    /// <see cref="SessionPolicy.Mode"/> is <see cref="SessionMode.Injection"/> and
    /// <see cref="SessionPolicy.EnableRedaction"/> is <see langword="false"/>, an
    /// <see cref="InvalidOperationException"/> is thrown immediately.
    /// </param>
    public SnoopInspector(
        Dispatcher dispatcher,
        object? rootTarget = null,
        SnoopInspectorOptions? options = null,
        SessionPolicy? sessionPolicy = null)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.rootTarget = rootTarget;
        this.options = options ?? new SnoopInspectorOptions();
        this.sessionPolicy = sessionPolicy;

        if (sessionPolicy is not null)
        {
            EnforceInjectionRedaction(sessionPolicy);
        }

        this.nodeRegistry = new NodeRegistry();
        this.cursorManager = new CursorManager();
        this.locatorResolver = new LocatorResolver(this.nodeRegistry);
    }

    /// <summary>
    /// Belt-and-braces MF-11 assertion: Injection mode must always have redaction enabled.
    /// Throws <see cref="InvalidOperationException"/> if a bypass <see cref="SessionPolicy"/>
    /// is supplied (i.e., constructed without going through <see cref="SessionPolicy.Create"/>).
    /// </summary>
    /// <param name="policy">The session policy to validate.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="policy"/> has <c>Mode == Injection</c> and
    /// <c>EnableRedaction == false</c>.
    /// </exception>
    public static void EnforceInjectionRedaction(SessionPolicy policy)
    {
        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        if (policy.Mode == SessionMode.Injection && !policy.EnableRedaction)
        {
            throw new InvalidOperationException(
                "Injection mode requires EnableRedaction=true (MF-11). Policy was constructed bypassing SessionPolicy.Create.");
        }
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
        this.disposeCts.Cancel();
        this.disposeCts.Dispose();
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
                                Title = w.Title ?? string.Empty,
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
    public Task<StateDeltaDto> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

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

            // M1-11: compute stateChanged at serialization time by reading the observable DP
            // value NOW, after any re-entrant PropertyChangedCallback has settled.  This closes
            // the false-positive where a reverting callback resets the value but the old code
            // compared previousValue to the *requested* convertedValue string (PRD §7.3 W3-C1).
            var stateChanged = StateDeltaSerializationHook.ComputeStateChanged(depObj, depProp, previousValue);

            // newValue reflects the actual settled observable state (not the requested string).
            var newValue = depObj.GetValue(depProp)?.ToString() ?? string.Empty;

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
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
    // ISnoopInspector — WpfLocator overloads (M1-06)
    // -------------------------------------------------------------------------

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

    /// <inheritdoc/>
    public async Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        WpfLocator locator,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetPropertiesAsync(nodeId, filter, category, includeDefaults, cursor, take, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string value,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetPropertyAsync(nodeId, propertyName, value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetBindingInfoAsync(nodeId, propertyName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        WpfLocator locator,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.RunDiagnosticsAsync(nodeId, providers, minLevel, cursor, take, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CursorPage<ResourceDto>> GetResourcesAsync(
        WpfLocator locator,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetResourcesAsync(nodeId, resourceKey, cursor, take, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.CaptureScreenshotAsync(nodeId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
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

    // ── M2-04a/M2-04b: wpf_select_item (L0 — non-virtualized + virtualized scroll) ──────────

    /// <summary>
    /// Maximum number of scroll-materialise iterations before giving up for virtualised lists.
    /// </summary>
    private const int VirtualizedScrollMaxIterations = 20;

    /// <inheritdoc/>
    public Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(identifier))
        {
            throw new ArgumentException("identifier must not be null or empty.", nameof(identifier));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_select_item.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.Controls.Primitives.Selector selector)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Resolve item index from identifier.
            int resolvedIndex;
            FailureReason selectFailureReason;
            if (!ResolveItemIndex(selector, identifier, out resolvedIndex, out selectFailureReason))
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = selectFailureReason,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(selectFailureReason, null),
                };
            }

            // ── M2-04b: virtualised path ─────────────────────────────────────────
            // When the selector uses a VirtualizingStackPanel, the container for
            // off-screen items may not yet be materialised. Scroll the item into
            // view first; the panel materialises containers on demand. We pump the
            // Dispatcher between scroll requests to allow WPF layout to run.
            // Budget: VirtualizedScrollMaxIterations attempts before we give up.
            if (IsVirtualizingSelector(selector) &&
                !IsItemContainerMaterialized(selector, resolvedIndex))
            {
                var materialized = ScrollMaterializeItem(selector, resolvedIndex);
                if (!materialized)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[SnoopWPF.Agent] SelectItem({selector.GetType().Name}): item at index {resolvedIndex} " +
                        $"not materialized after {VirtualizedScrollMaxIterations} scroll iterations — " +
                        "returning ElementOutsideViewport.");

                    return new StateDeltaDto
                    {
                        Success = false,
                        ElementVisible = true,
                        StateChanged = false,
                        FailureReason = FailureReason.ElementOutsideViewport,
                        Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.ElementOutsideViewport, null),
                    };
                }
            }

            var previousIndex = selector.SelectedIndex;
            var previousValue = previousIndex >= 0 && previousIndex < selector.Items.Count
                ? selector.Items[previousIndex]?.ToString()
                : null;

            selector.SetValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, resolvedIndex);

            var newIndex = selector.SelectedIndex;
            var newValue = newIndex >= 0 && newIndex < selector.Items.Count
                ? selector.Items[newIndex]?.ToString()
                : null;
            var stateChanged = previousIndex != newIndex;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SelectItem({selector.GetType().Name}): nodeId={nodeId}, index={resolvedIndex}, stateChanged={stateChanged}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <summary>
    /// Returns the ItemsHost panel for an <see cref="System.Windows.Controls.ItemsControl"/>
    /// using reflection (ItemsHost is an internal property in WPF).
    /// </summary>
    private static System.Windows.Controls.Panel? GetItemsHost(System.Windows.Controls.ItemsControl itemsControl)
    {
        var prop = typeof(System.Windows.Controls.ItemsControl)
            .GetProperty("ItemsHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(itemsControl) as System.Windows.Controls.Panel;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="selector"/> is backed by a
    /// <see cref="System.Windows.Controls.VirtualizingStackPanel"/> (i.e. UI-virtualisation
    /// is active and item containers may not yet be materialised).
    /// </summary>
    private static bool IsVirtualizingSelector(System.Windows.Controls.Primitives.Selector selector)
    {
        // VirtualizingPanel.IsVirtualizing attached DP is the canonical flag.
        var isVirtualizing = (bool)selector.GetValue(
            System.Windows.Controls.VirtualizingPanel.IsVirtualizingProperty);
        if (!isVirtualizing)
        {
            return false;
        }

        // Confirm the items host is actually a VirtualizingStackPanel.
        if (selector is System.Windows.Controls.ItemsControl itemsControl)
        {
            var panel = GetItemsHost(itemsControl);
            return panel is System.Windows.Controls.VirtualizingStackPanel;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the item container for
    /// <paramref name="index"/> is already materialised (the generator's status
    /// for that position is <see cref="System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated"/>
    /// and <see cref="System.Windows.Controls.Primitives.ItemContainerGenerator.ContainerFromIndex"/>
    /// returns a non-null element).
    /// </summary>
    private static bool IsItemContainerMaterialized(
        System.Windows.Controls.Primitives.Selector selector,
        int index)
    {
        var generator = selector.ItemContainerGenerator;
        if (generator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            return false;
        }

        return generator.ContainerFromIndex(index) != null;
    }

    /// <summary>
    /// Attempts to scroll-materialise the container for item at <paramref name="index"/>
    /// within <paramref name="selector"/> by issuing up to
    /// <see cref="VirtualizedScrollMaxIterations"/> <c>BringIndexIntoView</c> / <c>ScrollIntoView</c>
    /// calls and pumping the Dispatcher between each.
    /// Returns <see langword="true"/> when the container is materialised before the
    /// budget is exhausted.
    /// </summary>
    private static bool ScrollMaterializeItem(
        System.Windows.Controls.Primitives.Selector selector,
        int index)
    {
        for (var iteration = 0; iteration < VirtualizedScrollMaxIterations; iteration++)
        {
            // Request the panel to bring the item into view (materialises its container).
            if (selector is System.Windows.Controls.ItemsControl itemsControl)
            {
                itemsControl.UpdateLayout();

                // VirtualizingStackPanel.BringIndexIntoViewPublic is internal; use the
                // public ScrollViewer.ScrollIntoView path via ItemsControl.
                var panel = GetItemsHost(itemsControl) as System.Windows.Controls.VirtualizingStackPanel;
                if (panel != null)
                {
                    // Calling BringIndexIntoView on VirtualizingStackPanel scrolls the panel
                    // without requiring the container to already exist.
                    panel.BringIndexIntoViewPublic(index);
                }

                // Also ask the ScrollViewer (if present) to ensure visibility.
                if (selector is System.Windows.Controls.ListBox listBox)
                {
                    if (index >= 0 && index < listBox.Items.Count)
                    {
                        listBox.ScrollIntoView(listBox.Items[index]);
                    }
                }

                // Pump layout so the panel can materialise containers.
                itemsControl.UpdateLayout();
            }

            if (IsItemContainerMaterialized(selector, index))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SelectItemAsync(nodeId, identifier, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a zero-based item index from an <paramref name="identifier"/> string
    /// within the given <paramref name="selector"/>'s Items collection.
    /// Returns <see langword="true"/> when a unique match is found.
    /// </summary>
    private static bool ResolveItemIndex(
        System.Windows.Controls.Primitives.Selector selector,
        string identifier,
        out int resolvedIndex,
        out FailureReason failureReason)
    {
        // 1. Try numeric index.
        int numericIndex;
        if (int.TryParse(identifier, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out numericIndex))
        {
            if (numericIndex < 0 || numericIndex >= selector.Items.Count)
            {
                resolvedIndex = -1;
                failureReason = FailureReason.ElementNotFound;
                return false;
            }

            resolvedIndex = numericIndex;
            failureReason = default(FailureReason);
            return true;
        }

        // 2. Exact text match (case-insensitive).
        var exactMatches = new System.Collections.Generic.List<int>();
        var partialMatches = new System.Collections.Generic.List<int>();

        for (var i = 0; i < selector.Items.Count; i++)
        {
            var itemText = selector.Items[i]?.ToString() ?? string.Empty;

            if (string.Equals(itemText, identifier, StringComparison.OrdinalIgnoreCase))
            {
                exactMatches.Add(i);
            }
            else if (itemText.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                partialMatches.Add(i);
            }
        }

        if (exactMatches.Count == 1)
        {
            resolvedIndex = exactMatches[0];
            failureReason = default(FailureReason);
            return true;
        }

        if (exactMatches.Count > 1)
        {
            resolvedIndex = -1;
            failureReason = FailureReason.LocatorAmbiguous;
            return false;
        }

        // 3. Partial text match — unambiguous substring only.
        if (partialMatches.Count == 1)
        {
            resolvedIndex = partialMatches[0];
            failureReason = default(FailureReason);
            return true;
        }

        if (partialMatches.Count > 1)
        {
            resolvedIndex = -1;
            failureReason = FailureReason.LocatorAmbiguous;
            return false;
        }

        resolvedIndex = -1;
        failureReason = FailureReason.ElementNotFound;
        return false;
    }

    // ── M2-03: wpf_set_check_state ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(state))
        {
            throw new ArgumentException("state must not be null or empty.", nameof(state));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_check_state.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Reject bare ToggleButton (not CheckBox/RadioButton) — suggest wpf_toggle.
            if (depObj is System.Windows.Controls.Primitives.ToggleButton
                and not System.Windows.Controls.CheckBox
                and not System.Windows.Controls.RadioButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = new Contracts.Dtos.SuggestionDto
                    {
                        Tool = "wpf_toggle",
                        Args = new System.Collections.Generic.List<Contracts.Dtos.NameValuePairDto>
                        {
                            new() { Name = "nodeId", Value = nodeId },
                            new() { Name = "hint", Value = "Use wpf_toggle for bare ToggleButton controls." },
                        },
                    },
                };
            }

            // Only CheckBox and RadioButton are supported.
            if (depObj is not System.Windows.Controls.Primitives.ToggleButton toggleButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Parse the desired state.
            bool? desiredState;
            if (string.Equals(state, "checked", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = true;
            }
            else if (string.Equals(state, "unchecked", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = false;
            }
            else if (string.Equals(state, "indeterminate", StringComparison.OrdinalIgnoreCase))
            {
                desiredState = null;
            }
            else
            {
                throw new SnoopException(
                    SnoopErrorCode.TypeConversionFailed,
                    $"Invalid state value '{state}'. Expected \"checked\", \"unchecked\", or \"indeterminate\".",
                    targetId: nodeId);
            }

            var previousRaw = toggleButton.IsChecked;
            var previousValue = FormatChecked(previousRaw);

            toggleButton.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, desiredState);

            var newRaw = toggleButton.IsChecked;
            var newValue = FormatChecked(newRaw);
            var stateChanged = previousRaw != newRaw;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetCheckState({depObj.GetType().Name}): nodeId={nodeId}, previous={previousValue}, new={newValue}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetCheckStateAsync(nodeId, state, ct).ConfigureAwait(false);
    }

    private static string FormatChecked(bool? value) => value switch
    {
        true => "checked",
        false => "unchecked",
        null => "indeterminate",
    };

    // ── M2-02: wpf_set_text_value ─────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_text_value.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // ── TextBox ──────────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.TextBox textBox)
            {
                var previousValue = textBox.Text;
                textBox.SetValue(System.Windows.Controls.TextBox.TextProperty, value);
                var newValue = textBox.Text;
                var stateChanged = !string.Equals(previousValue, newValue, System.StringComparison.Ordinal);

                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] SetTextValue(TextBox): nodeId={nodeId}, stateChanged={stateChanged}");

                if (!stateChanged)
                {
                    return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                    {
                        ElementVisible = true,
                        PreviousValue = previousValue,
                        NewValue = newValue,
                        ChosenTier = InputTier.L0,
                    };
                }

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            // ── PasswordBox ──────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.PasswordBox passwordBox)
            {
                // SensitiveText (S3): never log or return the actual password value.
                const string redactedMarker = "[REDACTED]";

                // AllowSensitiveRetention gate: only return newValue when explicitly permitted.
                var allowRetention = this.options.AllowSensitiveRetention;

                System.Diagnostics.Trace.WriteLine(
                    "[SnoopWPF.Agent] SetTextValue(PasswordBox): value=<redacted>");

                // PasswordBox.Password is a CLR property backed by SecureString, not a standard DP.
                passwordBox.Password = value;

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = redactedMarker,
                    NewValue = allowRetention ? value : redactedMarker,
                    ChosenTier = InputTier.L0,
                };
            }

            // ── RichTextBox ──────────────────────────────────────────────────────
            if (depObj is System.Windows.Controls.RichTextBox richTextBox)
            {
                var startPointer = richTextBox.Document.ContentStart;
                var endPointer = richTextBox.Document.ContentEnd;
                var previousValue = new System.Windows.Documents.TextRange(startPointer, endPointer).Text ?? string.Empty;

                richTextBox.Document = new System.Windows.Documents.FlowDocument(
                    new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(value)));

                var newStartPointer = richTextBox.Document.ContentStart;
                var newEndPointer = richTextBox.Document.ContentEnd;
                var newValue = new System.Windows.Documents.TextRange(newStartPointer, newEndPointer).Text ?? string.Empty;
                var stateChanged = !string.Equals(previousValue, newValue, System.StringComparison.Ordinal);

                System.Diagnostics.Trace.WriteLine(
                    $"[SnoopWPF.Agent] SetTextValue(RichTextBox): nodeId={nodeId}, stateChanged={stateChanged}");

                if (!stateChanged)
                {
                    return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                    {
                        ElementVisible = true,
                        PreviousValue = previousValue,
                        NewValue = newValue,
                        ChosenTier = InputTier.L0,
                    };
                }

                return new StateDeltaDto
                {
                    Success = true,
                    ElementVisible = true,
                    StateChanged = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    ChosenTier = InputTier.L0,
                };
            }

            // Not a supported text control.
            return new StateDeltaDto
            {
                Success = false,
                ElementVisible = true,
                StateChanged = false,
                FailureReason = FailureReason.PatternNotSupported,
                Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetTextValueAsync(nodeId, value, ct).ConfigureAwait(false);
    }

    // ── M2-16: wpf_set_slider_value ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> SetSliderValueAsync(string nodeId, double value, bool normalized, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_set_slider_value.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.Controls.Primitives.RangeBase rangeBase)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = target is System.Windows.FrameworkElement,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            var previousValue = rangeBase.Value;

            double targetValue;
            if (normalized)
            {
                var fraction = Math.Max(0.0, Math.Min(1.0, value));
                var min = rangeBase.Minimum;
                var max = rangeBase.Maximum;
                var range = max - min;
                targetValue = range <= 0.0 ? min : min + (fraction * range);
            }
            else
            {
                targetValue = Math.Max(rangeBase.Minimum, Math.Min(rangeBase.Maximum, value));
            }

            rangeBase.SetValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, targetValue);

            var newValue = rangeBase.Value;
            var stateChanged = Math.Abs(previousValue - newValue) > double.Epsilon;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetSliderValue({rangeBase.GetType().Name}): " +
                $"normalized={normalized}, input={value}, target={targetValue}, stateChanged={stateChanged}");

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = newValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ChosenTier = InputTier.L0,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValue = newValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetSliderValueAsync(WpfLocator locator, double value, bool normalized, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetSliderValueAsync(nodeId, value, normalized, ct).ConfigureAwait(false);
    }

    // ── M2-01: wpf_execute_command ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled (L0 execute = mutation).
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions to allow wpf_execute_command.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not DependencyObject depObj)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Resolve Command DP (ButtonBase.CommandProperty is the canonical L0 DP).
            var command = depObj.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)
                          as System.Windows.Input.ICommand;

            if (command is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Capture previousValue: window count acts as a coarse state proxy.
            var windowCountBefore = System.Windows.Application.Current?.Windows.Count ?? 0;
            var previousValue = windowCountBefore.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Resolve optional CommandParameter.
            var commandParameter = depObj.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandParameterProperty);

            // CanExecute gate.
            if (!command.CanExecute(commandParameter))
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.CannotExecuteCommand,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.CannotExecuteCommand, null),
                    PreviousValue = previousValue,
                };
            }

            // Execute the command.
            command.Execute(commandParameter);

            // Compute stateChanged: compare window count before vs after.
            var windowCountAfter = System.Windows.Application.Current?.Windows.Count ?? 0;
            var treeVersionDelta = windowCountAfter - windowCountBefore;
            var stateChanged = treeVersionDelta != 0;

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ExecuteCommand: nodeId={nodeId}, windowDelta={treeVersionDelta}");

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = stateChanged,
                TreeVersionDelta = treeVersionDelta,
                PreviousValue = previousValue,
                ChosenTier = InputTier.L0,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ExecuteCommandAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-05: wpf_click ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_click.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Obtain automation peer and IInvokeProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var invokeProvider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.Invoke)
                as System.Windows.Automation.Provider.IInvokeProvider;

            if (invokeProvider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Detect whether a Command is bound — if so, suggest wpf_execute_command (L0 preferred).
            var hasCommandBound =
                uiElement.GetValue(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)
                is System.Windows.Input.ICommand;

            // Invoke via IInvokeProvider.
            invokeProvider.Invoke();

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ClickAsync: nodeId={nodeId}, hasCommandBound={hasCommandBound}");

            // When a Command is bound, attach a hint suggesting wpf_execute_command (L0).
            SuggestionDto? suggestion = hasCommandBound
                ? new SuggestionDto
                {
                    Tool = "wpf_execute_command",
                    Args = new System.Collections.Generic.List<NameValuePairDto>
                    {
                        new() { Name = "nodeId", Value = nodeId },
                        new()
                        {
                            Name = "hint",
                            Value = "Element has a Command bound; prefer wpf_execute_command (L0) over wpf_click (L1).",
                        },
                    },
                }
                : null;

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
                Suggestion = suggestion,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ClickAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-06: wpf_toggle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_toggle.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Reject CheckBox and RadioButton — suggest wpf_set_check_state (L0 preferred for deterministic state).
            if (uiElement is System.Windows.Controls.CheckBox or System.Windows.Controls.RadioButton)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = new SuggestionDto
                    {
                        Tool = "wpf_set_check_state",
                        Args = new System.Collections.Generic.List<NameValuePairDto>
                        {
                            new() { Name = "nodeId", Value = nodeId },
                            new()
                            {
                                Name = "hint",
                                Value = "Use wpf_set_check_state (L0) for CheckBox/RadioButton to specify a deterministic target state.",
                            },
                        },
                    },
                };
            }

            // Obtain automation peer and IToggleProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var toggleProvider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.Toggle)
                as System.Windows.Automation.Provider.IToggleProvider;

            if (toggleProvider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Toggle via IToggleProvider — flips current state (non-deterministic).
            toggleProvider.Toggle();

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ToggleAsync: nodeId={nodeId}, type={uiElement.GetType().Name}");

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ToggleAsync(nodeId, ct).ConfigureAwait(false);
    }

    // ── M2-07: wpf_expand_collapse ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(action) ||
            (!string.Equals(action, "expand", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(action, "collapse", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("action must be \"expand\" or \"collapse\".", nameof(action));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: automation must be explicitly enabled (L1 requires EnableAutomation).
            if (!this.options.EnableAutomation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Automation is disabled. Set EnableAutomation=true in SnoopInspectorOptions to allow wpf_expand_collapse.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            if (target is not System.Windows.UIElement uiElement)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = false,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            // Obtain automation peer and IExpandCollapseProvider.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(uiElement);
            var provider = peer?.GetPattern(
                System.Windows.Automation.Peers.PatternInterface.ExpandCollapse)
                as System.Windows.Automation.Provider.IExpandCollapseProvider;

            if (provider is null)
            {
                return new StateDeltaDto
                {
                    Success = false,
                    ElementVisible = true,
                    StateChanged = false,
                    FailureReason = FailureReason.PatternNotSupported,
                    Suggestion = FailureReasonDescriptor.Suggest(FailureReason.PatternNotSupported, null),
                };
            }

            bool expand = string.Equals(action, "expand", StringComparison.OrdinalIgnoreCase);
            if (expand)
            {
                provider.Expand();
            }
            else
            {
                provider.Collapse();
            }

            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] ExpandCollapseAsync: nodeId={nodeId}, action={action}, type={uiElement.GetType().Name}");

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                ChosenTier = InputTier.L1,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ExpandCollapseAsync(nodeId, action, ct).ConfigureAwait(false);
    }

    // ── M2-08: wpf_resolve_binding ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("propertyName must not be null or empty.", nameof(propertyName));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);
            return this.bindingResolver.Resolve(target, propertyName);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.ResolveBindingAsync(nodeId, propertyName, ct).ConfigureAwait(false);
    }

    // ── M2-09: wpf_wait_for_property ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Contracts.Dtos.WaitForPropertyResultDto> WaitForPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string? expectedValue,
        int timeoutMs,
        string presenceExpected,
        CancellationToken ct)
    {
        if (locator is null)
        {
            throw new ArgumentNullException(nameof(locator));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("propertyName must not be null or empty.", nameof(propertyName));
        }

        var absent = string.Equals(presenceExpected, "absent", StringComparison.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(timeoutMs);
        var pollCount = 0;
        const int MinPollIntervalMs = 50;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            pollCount++;

            string? actualValue = null;
            bool elementFound = false;
            bool dispatcherBusy = false;

            try
            {
                var pollResult = await this.RunOnDispatcherAsync(() =>
                {
                    var root = this.GetEffectiveRootTarget();
                    var resolved = this.locatorResolver.TryResolve(locator, root);
                    if (resolved is null)
                    {
                        return new WaitForPropertyPollResult(false, null);
                    }

                    var props = PropertyInformation.GetProperties(resolved);
                    try
                    {
                        var match = props.FirstOrDefault(p =>
                            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                        return new WaitForPropertyPollResult(true, match?.StringValue);
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

            if (!dispatcherBusy)
            {
                bool conditionMet = absent
                    ? !elementFound
                    : elementFound && string.Equals(actualValue, expectedValue, StringComparison.Ordinal);

                if (conditionMet)
                {
                    return new Contracts.Dtos.WaitForPropertyResultDto
                    {
                        ConditionMet = true,
                        ActualValue = actualValue,
                        ElapsedMs = (int)sw.ElapsedMilliseconds,
                        PollCount = pollCount,
                    };
                }
            }

            if (sw.Elapsed >= deadline)
            {
                throw new SnoopException(
                    SnoopErrorCode.DispatcherBusy,
                    $"wpf_wait_for_property timed out after {timeoutMs}ms waiting for " +
                    $"'{propertyName}' {(absent ? "to disappear" : $"= \"{expectedValue}\"")}. " +
                    $"Last observed value: {(actualValue is null ? "<null>" : $"\"{actualValue}\"")}.",
                    suggestions: new[] { SnoopSuggestions.WaitForPropertyTimeout });
            }

            var remaining = (int)(deadline - sw.Elapsed).TotalMilliseconds;
            var delay = Math.Min(MinPollIntervalMs, Math.Max(1, remaining - 1));
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    // ── M2-11: wpf_pump_until_idle ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Contracts.Dtos.PumpUntilIdleResultDto> PumpUntilIdleAsync(
        int timeoutMs,
        IReadOnlyList<string>? resources,
        CancellationToken ct)
    {
        this.ThrowIfDisposed();

        // Nested-pump guard (PRD §8.2): reject concurrent calls regardless of thread.
        // Interlocked.CompareExchange atomically sets pumpInProgress to 1 if it was 0.
        if (Interlocked.CompareExchange(ref this.pumpInProgress, 1, 0) != 0)
        {
            throw new SnoopException(
                SnoopErrorCode.DispatcherBusy,
                "wpf_pump_until_idle cannot be called re-entrantly: a pump is already in progress.",
                suggestions: new[] { SnoopSuggestions.DispatcherBusy });
        }

        // 5-second animation-runaway ceiling (PRD §8.2 W3-H2).
        const int MaxTimeoutMs = 5000;
        var clampedTimeout = Math.Min(timeoutMs, MaxTimeoutMs);

        try
        {
            return await this.PumpUntilIdleCoreAsync(clampedTimeout, resources, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref this.pumpInProgress, 0);
        }
    }

    private async Task<Contracts.Dtos.PumpUntilIdleResultDto> PumpUntilIdleCoreAsync(
        int timeoutMs,
        IReadOnlyList<string>? resources,
        CancellationToken ct)
    {
        // Build the set of resource names to monitor (null/empty = all built-in resources).
        var filterNames = resources is { Count: > 0 }
            ? new HashSet<string>(resources, StringComparer.OrdinalIgnoreCase)
            : null;

        // Instantiate built-in resources on the Dispatcher thread so they can observe it.
        // Only DispatcherIdlingResource and CompositionRenderingResource can be created without
        // a specific object instance to observe — they monitor the Dispatcher queue and
        // rendering pipeline respectively.
        var builtInResources = await this.RunOnDispatcherAsync<List<IIdlingResource>>(() =>
        {
            var list = new List<IIdlingResource>
            {
                new DispatcherIdlingResource(this.dispatcher),
                new CompositionRenderingResource(this.dispatcher),
            };

            // Apply resource name filter if specified.
            if (filterNames is not null)
            {
                list = list.FindAll(r => filterNames.Contains(r.Name));
            }

            return list;
        }, ct).ConfigureAwait(false);

        var monitored = builtInResources.ConvertAll(r => r.Name);

        // Build an IdlingResourceRegistry for the AND-gate.
        using var registry = new IdlingResourceRegistry();
        foreach (var r in builtInResources)
        {
            registry.Register(r);
        }

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(timeoutMs);

        try
        {
            // Fast path: already idle.
            if (registry.IsIdle)
            {
                return new Contracts.Dtos.PumpUntilIdleResultDto
                {
                    IdleReached = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ResourcesMonitored = monitored,
                    StillBusy = new List<string>(),
                };
            }

            // Wait for the registry IdleChanged event or timeout.
            using var idleSignal = new SemaphoreSlim(0, 1);

            void OnIdleChanged(object? sender, IdleChangedEventArgs e)
            {
                if (e.IsIdle)
                {
                    try
                    {
                        idleSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // Already signalled — ignore.
                    }
                    catch (ObjectDisposedException)
                    {
                        // idleSignal was disposed (cancellation raced with idle-change) — ignore.
                    }
                }
            }

            registry.IdleChanged += OnIdleChanged;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Check again after subscribing to avoid a TOCTOU race.
                    if (registry.IsIdle)
                    {
                        return new Contracts.Dtos.PumpUntilIdleResultDto
                        {
                            IdleReached = true,
                            ElapsedMs = (int)sw.ElapsedMilliseconds,
                            ResourcesMonitored = monitored,
                            StillBusy = new List<string>(),
                        };
                    }

                    var remaining = (int)(deadline - sw.Elapsed).TotalMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    // Wait up to remaining ms for an idle signal.
                    var signalled = await idleSignal.WaitAsync(remaining, ct).ConfigureAwait(false);
                    if (signalled && registry.IsIdle)
                    {
                        return new Contracts.Dtos.PumpUntilIdleResultDto
                        {
                            IdleReached = true,
                            ElapsedMs = (int)sw.ElapsedMilliseconds,
                            ResourcesMonitored = monitored,
                            StillBusy = new List<string>(),
                        };
                    }

                    // Check elapsed again (handles the case where we were woken but not yet fully idle).
                    if (sw.Elapsed >= deadline)
                    {
                        break;
                    }
                }
            }
            finally
            {
                registry.IdleChanged -= OnIdleChanged;
            }

            // Timeout reached — identify still-busy resources and throw.
            var stillBusy = builtInResources.FindAll(r => !r.IsIdle).ConvertAll(r => r.Name);

            throw new SnoopException(
                SnoopErrorCode.DispatcherBusy,
                $"wpf_pump_until_idle timed out after {timeoutMs}ms waiting for idle. " +
                $"Still busy: [{string.Join(", ", stillBusy)}].",
                suggestions: new[] { SnoopSuggestions.DispatcherBusy });
        }
        finally
        {
            // Dispose all built-in resource instances we created.
            foreach (var r in builtInResources)
            {
                if (r is IDisposable d)
                {
                    d.Dispose();
                }
            }
        }
    }

    // ── M2-10: wpf_poll_changes ────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Contracts.Dtos.PollChangesResultDto> PollChangesAsync(
        long sinceVersion,
        WpfLocator? rootLocator,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            // Walk the live visual tree from the effective root (or locator root).
            var rootTarget = rootLocator is not null
                ? (object?)this.locatorResolver.TryResolve(rootLocator, this.GetEffectiveRootTarget())
                : this.GetEffectiveRootTarget();

            // Collect all nodeIds currently visible in the live tree.
            // CollectLiveNodeIds uses GetExistingId — does NOT register new nodes or
            // increment the version counter. Only nodes already known to the registry
            // (registered by prior operations such as GetVisualTree, FindElements, etc.)
            // appear in liveNodeIds. This keeps the version stable across back-to-back
            // polls when no external mutations have occurred.
            var liveNodeIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (rootTarget is not null)
            {
                this.CollectLiveNodeIds(rootTarget, liveNodeIds);
            }

            // Snapshot version after the walk (GetExistingId does not increment, so
            // this equals Version before the walk). Used as the returned baseline.
            var currentVersion = this.nodeRegistry.Version;

            // "added" = in live tree AND registered after sinceVersion.
            var changes = new List<Contracts.Dtos.NodeChangeEntryDto>();

            foreach (var nodeId in liveNodeIds)
            {
                if (TryParseNodeVersion(nodeId, out var nodeVersion)
                    && nodeVersion > sinceVersion)
                {
                    changes.Add(new Contracts.Dtos.NodeChangeEntryDto
                    {
                        NodeId = nodeId,
                        ChangeKind = "added",
                    });
                }
            }

            // "removed" — prefer snapshot-based diff when we have a cached live set from the
            // previous poll at exactly sinceVersion (incremental case).  Fall back to the
            // registry-scan (CollectRemovedSince) for the initial / catch-up case.
            IEnumerable<string> removedIds;
            if (this.lastPollVersion == sinceVersion && this.lastPollLiveIds is not null)
            {
                // Incremental: anything that was alive last time but is not alive now.
                removedIds = this.lastPollLiveIds
                    .Where(id => !liveNodeIds.Contains(id))
                    .ToList();
            }
            else
            {
                // Catch-up: fall back to registry scan for nodes registered <= sinceVersion.
                removedIds = this.nodeRegistry.CollectRemovedSince(sinceVersion, liveNodeIds);
            }

            foreach (var removedId in removedIds)
            {
                changes.Add(new Contracts.Dtos.NodeChangeEntryDto
                {
                    NodeId = removedId,
                    ChangeKind = "removed",
                });
            }

            // Cache this poll's live snapshot so the next incremental poll can diff against it.
            this.lastPollVersion = currentVersion;
            this.lastPollLiveIds = liveNodeIds;

            return new Contracts.Dtos.PollChangesResultDto
            {
                TreeVersion = currentVersion,
                SinceVersion = sinceVersion,
                Changes = changes,
                ChangeCount = changes.Count,
            };
        }, ct);
    }

    /// <summary>
    /// Parses the sequence number from a node ID in the format "0:&lt;n&gt;".
    /// </summary>
    private static bool TryParseNodeVersion(string nodeId, out long version)
    {
        version = 0;
        // Use string overload to satisfy CA1307 across all TFMs.
        var colonIdx = nodeId.IndexOf(":", StringComparison.Ordinal);
        if (colonIdx < 0)
        {
            return false;
        }

        return long.TryParse(nodeId.Substring(colonIdx + 1), out version);
    }

    /// <summary>
    /// Recursively walks the visual tree from <paramref name="root"/> and collects
    /// stable node IDs for all reachable objects that are already registered in the
    /// NodeRegistry. Must be called on the Dispatcher thread.
    ///
    /// Deliberately uses <see cref="NodeRegistry.GetExistingId"/> (not GetOrCreateId)
    /// so that the tree walk does NOT increment the version counter or register new
    /// nodes. This keeps the treeVersion stable across back-to-back poll calls when
    /// the tree has not actually mutated.
    /// </summary>
    private void CollectLiveNodeIds(object root, System.Collections.Generic.HashSet<string> ids)
    {
        const int maxNodes = 5000;
        // visitedObjects prevents re-queuing the same object even when it has no
        // registered ID yet (avoids infinite loops through unregistered subtrees).
#if NET5_0_OR_GREATER
        var visitedObjects = new System.Collections.Generic.HashSet<object>(
            ReferenceEqualityComparer.Instance);
#else
        var visitedObjects = new System.Collections.Generic.HashSet<object>(
            ObjectReferenceEqualityComparer.Instance);
#endif
        var queue = new Queue<object>();
        queue.Enqueue(root);

        while (queue.Count > 0 && ids.Count < maxNodes)
        {
            var current = queue.Dequeue();
            if (current is null || !visitedObjects.Add(current))
            {
                continue;
            }

            // Only include nodes that are already in the registry.
            // GetExistingId does NOT create new registrations.
            var id = this.nodeRegistry.GetExistingId(current);
            if (id is not null)
            {
                ids.Add(id);
            }

            // Walk visual children.
            if (current is System.Windows.Media.Visual visual)
            {
                var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual);
                for (var i = 0; i < childCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(visual, i);
                    if (child is not null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            else if (current is System.Windows.Application app)
            {
                foreach (System.Windows.Window w in app.Windows)
                {
                    if (w is not null)
                    {
                        queue.Enqueue(w);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolves a <see cref="WpfLocator"/> to a stable node ID by delegating to
    /// <see cref="LocatorResolver"/> on the WPF Dispatcher thread.
    /// Throws <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/>
    /// if the 100-entry NodeRegistry growth cap is exceeded.
    /// </summary>
    private Task<string> ResolveLocatorAsync(WpfLocator locator, CancellationToken ct)
    {
        if (locator is null)
        {
            throw new ArgumentNullException(nameof(locator));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            var root = this.GetEffectiveRootTarget();
            return this.locatorResolver.Resolve(locator, root);
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

        // FX-C1: link caller CT with dispose CTS so WaitAsync throws OperationCanceledException
        // (not ObjectDisposedException) if Dispose() races with this call.
        using var semWaitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, this.disposeCts.Token);
        await this.concurrencySemaphore.WaitAsync(semWaitCts.Token).ConfigureAwait(false);

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
            // FX2-C1: Dispose() may have run on another thread between acquiring and
            // releasing the slot. SemaphoreSlim.Release on a disposed instance throws
            // ObjectDisposedException → would surface as an unobserved task exception
            // (AppDomain-fatal on net462). Swallowing ODE is the correct behaviour here
            // because the disposal path has already released all backing resources.
            try
            {
                this.concurrencySemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Inspector was disposed while this call was in flight; nothing to release.
            }
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

    /// <summary>
    /// FX-M8: Returns a TierMismatch failure result if the session policy's MaxTier is below
    /// <see cref="InputTier.L1"/> (mutation tier). Call at the top of every mutation method.
    /// Returns <see langword="null"/> when the tier check passes.
    /// </summary>
    private StateDeltaDto? EnsureMutationTier()
    {
        if (this.sessionPolicy is not null && this.sessionPolicy.MaxTier < InputTier.L1)
        {
            return new StateDeltaDto
            {
                Success = false,
                ElementVisible = false,
                StateChanged = false,
                FailureReason = FailureReason.TierMismatch,
                Suggestion = StateDelta.FailureReasonDescriptor.Suggest(FailureReason.TierMismatch, null),
            };
        }

        return null;
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

    // -------------------------------------------------------------------------
    // Private nested types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Avoids ValueTuple syntax (not available in net462 without a NuGet polyfill)
    /// when returning two values from a Dispatcher-marshalled lambda inside
    /// <see cref="WaitForPropertyAsync"/>.
    /// </summary>
    private readonly struct WaitForPropertyPollResult
    {
        public WaitForPropertyPollResult(bool found, string? value)
        {
            this.Found = found;
            this.Value = value;
        }

        public bool Found { get; }

        public string? Value { get; }
    }
}
