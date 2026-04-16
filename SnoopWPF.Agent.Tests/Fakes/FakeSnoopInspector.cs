namespace SnoopWPF.Agent.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Configurable fake implementation of <see cref="ISnoopInspector"/> for unit tests.
/// Each method dispatches to a delegate property that tests can set.
/// If the delegate is null, throws <see cref="NotImplementedException"/>.
/// </summary>
public sealed class FakeSnoopInspector : ISnoopInspector
{
    // ---------- delegates for each method ----------
#pragma warning disable SA1201

    public Func<CancellationToken, Task<SessionInfoDto>>? OnGetSessionInfo { get; set; }

    public Func<bool, CancellationToken, Task<List<WindowDto>>>? OnGetWindows { get; set; }

    public Func<string?, int, string, List<string>?, CancellationToken, Task<VisualTreeResultDto>>? OnGetVisualTree { get; set; }

    public Func<string?, string, string?, int, CancellationToken, Task<CursorPage<NodeDto>>>? OnGetChildren { get; set; }

    public Func<string, int?, CancellationToken, Task<List<AncestorDto>>>? OnGetAncestors { get; set; }

    public Func<string?, string?, string?, List<PropertyConditionDto>?, string, int, CancellationToken, Task<FindElementResultDto>>? OnFindElements { get; set; }

    public Func<string, CancellationToken, Task<InspectElementDto>>? OnInspectElement { get; set; }

    public Func<string, string?, string?, bool, string?, int, CancellationToken, Task<CursorPage<PropertyDto>>>? OnGetProperties { get; set; }

    public Func<string, string, string, CancellationToken, Task<StateDeltaDto>>? OnSetProperty { get; set; }

    public Func<string, string, CancellationToken, Task<BindingInfoDto>>? OnGetBindingInfo { get; set; }

    public Func<string?, List<string>?, string?, string?, int, CancellationToken, Task<CursorPage<DiagnosticItemDto>>>? OnRunDiagnostics { get; set; }

    public Func<string?, string?, string?, int, CancellationToken, Task<CursorPage<ResourceDto>>>? OnGetResources { get; set; }

    public Func<string?, CancellationToken, Task<ScreenshotResultDto>>? OnCaptureScreenshot { get; set; }

    public Func<string, CancellationToken, Task<List<TriggerDto>>>? OnGetTriggers { get; set; }

    public Func<string, CancellationToken, Task<List<BehaviorDto>>>? OnGetBehaviors { get; set; }

    public Func<string, string, CancellationToken, Task<StateDeltaDto>>? OnSetCheckState { get; set; }

    public Func<string, string, CancellationToken, Task<StateDeltaDto>>? OnSetTextValue { get; set; }

    public Func<string, CancellationToken, Task<StateDeltaDto>>? OnExecuteCommand { get; set; }

    public Func<string, string, CancellationToken, Task<StateDeltaDto>>? OnSelectItem { get; set; }

#pragma warning restore SA1201

    // ---------- ISnoopInspector implementation ----------

    public Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct)
        => (this.OnGetSessionInfo ?? throw new NotImplementedException("OnGetSessionInfo not set")).Invoke(ct);

    public Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct)
        => (this.OnGetWindows ?? throw new NotImplementedException("OnGetWindows not set")).Invoke(includeHidden, ct);

    public Task<VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct)
        => (this.OnGetVisualTree ?? throw new NotImplementedException("OnGetVisualTree not set"))
            .Invoke(rootNodeId, maxDepth, treeType, includeProperties, ct);

    public Task<CursorPage<NodeDto>> GetChildrenAsync(
        string? nodeId, string treeType, string? cursor, int take, CancellationToken ct)
        => (this.OnGetChildren ?? throw new NotImplementedException("OnGetChildren not set"))
            .Invoke(nodeId, treeType, cursor, take, ct);

    public Task<List<AncestorDto>> GetAncestorsAsync(string nodeId, int? maxLevels, CancellationToken ct)
        => (this.OnGetAncestors ?? throw new NotImplementedException("OnGetAncestors not set"))
            .Invoke(nodeId, maxLevels, ct);

    public Task<FindElementResultDto> FindElementsAsync(
        string? typeName, string? name, string? rootNodeId,
        List<PropertyConditionDto>? conditions, string treeType, int maxResults, CancellationToken ct)
        => (this.OnFindElements ?? throw new NotImplementedException("OnFindElements not set"))
            .Invoke(typeName, name, rootNodeId, conditions, treeType, maxResults, ct);

    public Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct)
        => (this.OnInspectElement ?? throw new NotImplementedException("OnInspectElement not set"))
            .Invoke(nodeId, ct);

    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId, string? filter, string? category, bool includeDefaults,
        string? cursor, int take, CancellationToken ct)
        => (this.OnGetProperties ?? throw new NotImplementedException("OnGetProperties not set"))
            .Invoke(nodeId, filter, category, includeDefaults, cursor, take, ct);

    public Task<StateDeltaDto> SetPropertyAsync(
        string nodeId, string propertyName, string value, CancellationToken ct)
        => (this.OnSetProperty ?? throw new NotImplementedException("OnSetProperty not set"))
            .Invoke(nodeId, propertyName, value, ct);

    public Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct)
        => (this.OnGetBindingInfo ?? throw new NotImplementedException("OnGetBindingInfo not set"))
            .Invoke(nodeId, propertyName, ct);

    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct)
        => (this.OnRunDiagnostics ?? throw new NotImplementedException("OnRunDiagnostics not set"))
            .Invoke(nodeId, providers, minLevel, cursor, take, ct);

    public Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId, string? resourceKey, string? cursor, int take, CancellationToken ct)
        => (this.OnGetResources ?? throw new NotImplementedException("OnGetResources not set"))
            .Invoke(nodeId, resourceKey, cursor, take, ct);

    public Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct)
        => (this.OnCaptureScreenshot ?? throw new NotImplementedException("OnCaptureScreenshot not set"))
            .Invoke(nodeId, ct);

    public Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct)
        => (this.OnGetTriggers ?? throw new NotImplementedException("OnGetTriggers not set"))
            .Invoke(nodeId, ct);

    public Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct)
        => (this.OnGetBehaviors ?? throw new NotImplementedException("OnGetBehaviors not set"))
            .Invoke(nodeId, ct);

    // ── WpfLocator overloads (M1-06 stubs) ──

    public Task<VisualTreeResultDto> GetVisualTreeAsync(WpfLocator locator, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<CursorPage<NodeDto>> GetChildrenAsync(WpfLocator locator, string treeType, string? cursor, int take, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<List<AncestorDto>> GetAncestorsAsync(WpfLocator locator, int? maxLevels, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<InspectElementDto> InspectElementAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(WpfLocator locator, string? filter, string? category, bool includeDefaults, string? cursor, int take, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<StateDeltaDto> SetPropertyAsync(WpfLocator locator, string propertyName, string value, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(WpfLocator locator, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<CursorPage<ResourceDto>> GetResourcesAsync(WpfLocator locator, string? resourceKey, string? cursor, int take, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<List<TriggerDto>> GetTriggersAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<List<BehaviorDto>> GetBehaviorsAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M1-06");

    public Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct)
        => throw new NotImplementedException("M2-08");

    public Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct)
        => throw new NotImplementedException("M2-08");

    public Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct)
        => (this.OnSelectItem ?? throw new NotImplementedException("OnSelectItem not set"))
            .Invoke(nodeId, identifier, ct);

    public Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct)
        => throw new NotImplementedException("M2-04a");

    public Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct)
        => (this.OnSetCheckState ?? throw new NotImplementedException("OnSetCheckState not set"))
            .Invoke(nodeId, state, ct);

    public Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct)
        => throw new NotImplementedException("M2-03");

    public Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct)
        => (this.OnSetTextValue ?? throw new NotImplementedException("OnSetTextValue not set"))
            .Invoke(nodeId, value, ct);

    public Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct)
        => throw new NotImplementedException("M2-02");

    public Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct)
        => (this.OnExecuteCommand ?? throw new NotImplementedException("OnExecuteCommand not set"))
            .Invoke(nodeId, ct);

    public Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M2-01");

    public Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct)
        => throw new NotImplementedException("M2-05");

    public Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M2-05");

    public Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct)
        => throw new NotImplementedException("M2-06");

    public Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct)
        => throw new NotImplementedException("M2-06");

    public Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct)
        => throw new NotImplementedException("M2-07");

    public Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct)
        => throw new NotImplementedException("M2-07");

    public Task<WaitForPropertyResultDto> WaitForPropertyAsync(
        WpfLocator locator, string propertyName, string? expectedValue, int timeoutMs, string presenceExpected, CancellationToken ct)
        => throw new NotImplementedException("M2-09");

    public Task<PollChangesResultDto> PollChangesAsync(long sinceVersion, WpfLocator? rootLocator, CancellationToken ct)
        => throw new NotImplementedException("M2-10");
}
