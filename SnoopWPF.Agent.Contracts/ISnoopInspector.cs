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

    Task<SetPropertyResultDto> SetPropertyAsync(
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
}
