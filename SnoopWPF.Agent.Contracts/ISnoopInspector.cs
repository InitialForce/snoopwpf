namespace SnoopWPF.Agent.Contracts;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// The central inspection interface implemented by <c>SnoopInspector</c> (engine, in-process)
/// and <c>PipeSnoopInspectorProxy</c> (remote, injection mode).
/// All methods are Task-based and accept a <see cref="CancellationToken"/>.
/// </summary>
public interface ISnoopInspector
{
    Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct);

    Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct);

    Task<VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct);

    Task<CursorPage<NodeDto>> GetChildrenAsync(
        string? nodeId,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct);

    Task<List<AncestorDto>> GetAncestorsAsync(
        string nodeId,
        int? maxLevels,
        CancellationToken ct);

    Task<FindElementResultDto> FindElementsAsync(
        string? typeName,
        string? name,
        string? rootNodeId,
        List<PropertyConditionDto>? conditions,
        string treeType,
        int maxResults,
        CancellationToken ct);

    Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct);

    Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct);

    Task<StateDeltaDto> SetPropertyAsync(
        string nodeId,
        string propertyName,
        string value,
        CancellationToken ct);

    Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct);

    Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct);

    Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct);

    Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct);

    Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct);

    Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct);

    // ── WpfLocator overloads (M1-06) ──────────────────────────────────────────
    // Each method that accepts a nodeId gains a parallel WpfLocator overload.
    // Existing nodeId overloads are retained for compatibility.
    // Resolution is delegated to LocatorResolver which caps new NodeRegistry
    // entries at 100 per call; exceeding the cap surfaces LOCATOR_AMBIGUOUS.

    /// <summary>Locator overload for <see cref="GetVisualTreeAsync(string?,int,string,List{string}?,CancellationToken)"/>.</summary>
    Task<VisualTreeResultDto> GetVisualTreeAsync(
        WpfLocator locator,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetChildrenAsync(string?,string,string?,int,CancellationToken)"/>.</summary>
    Task<CursorPage<NodeDto>> GetChildrenAsync(
        WpfLocator locator,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetAncestorsAsync(string,int?,CancellationToken)"/>.</summary>
    Task<List<AncestorDto>> GetAncestorsAsync(
        WpfLocator locator,
        int? maxLevels,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="InspectElementAsync(string,CancellationToken)"/>.</summary>
    Task<InspectElementDto> InspectElementAsync(WpfLocator locator, CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetPropertiesAsync(string,string?,string?,bool,string?,int,CancellationToken)"/>.</summary>
    Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        WpfLocator locator,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="SetPropertyAsync(string,string,string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> SetPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string value,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetBindingInfoAsync(string,string,CancellationToken)"/>.</summary>
    Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct);

    /// <summary>Locator overload for <see cref="RunDiagnosticsAsync(string?,List{string}?,string?,string?,int,CancellationToken)"/>.</summary>
    Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        WpfLocator locator,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetResourcesAsync(string?,string?,string?,int,CancellationToken)"/>.</summary>
    Task<CursorPage<ResourceDto>> GetResourcesAsync(
        WpfLocator locator,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Locator overload for <see cref="CaptureScreenshotAsync(string?,CancellationToken)"/>.</summary>
    Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetTriggersAsync(string,CancellationToken)"/>.</summary>
    Task<List<TriggerDto>> GetTriggersAsync(WpfLocator locator, CancellationToken ct);

    /// <summary>Locator overload for <see cref="GetBehaviorsAsync(string,CancellationToken)"/>.</summary>
    Task<List<BehaviorDto>> GetBehaviorsAsync(WpfLocator locator, CancellationToken ct);

    // ── M2-08: wpf_resolve_binding ────────────────────────────────────────────

    /// <summary>
    /// Returns the full binding chain for <paramref name="propertyName"/> on the element
    /// identified by <paramref name="nodeId"/>: path, source, intermediate values, converter,
    /// mode, validation errors, and overall status (M2-08).
    /// </summary>
    Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ResolveBindingAsync(string,string,CancellationToken)"/>.</summary>
    Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct);
}
