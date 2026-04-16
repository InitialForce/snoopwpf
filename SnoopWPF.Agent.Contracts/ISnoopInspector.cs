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

    // ── M2-03: wpf_set_check_state ───────────────────────────────────────────

    /// <summary>
    /// Sets the <c>IsChecked</c> state of a <c>CheckBox</c> or <c>RadioButton</c>
    /// identified by <paramref name="nodeId"/> via <c>SetValue(ToggleButton.IsCheckedProperty, …)</c>
    /// (L0, M2-03). Accepts <c>"checked"</c>, <c>"unchecked"</c>, or <c>"indeterminate"</c>.
    /// Bare <c>ToggleButton</c> (not CheckBox/RadioButton) is rejected with
    /// <see cref="FailureReason.PatternNotSupported"/> and a <c>wpf_toggle</c> suggestion.
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct);

    /// <summary>Locator overload for <see cref="SetCheckStateAsync(string,string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct);

    // ── M2-02: wpf_set_text_value ─────────────────────────────────────────────

    /// <summary>
    /// Sets the text content of a <c>TextBox</c>, <c>PasswordBox</c>, or <c>RichTextBox</c>
    /// identified by <paramref name="nodeId"/> via <c>SetValue</c> on the control's text DP (L0, M2-02).
    /// PasswordBox input is treated as SensitiveText (S3) — previousValue is always redacted.
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct);

    /// <summary>Locator overload for <see cref="SetTextValueAsync(string,string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct);

    // ── M2-01: wpf_execute_command ────────────────────────────────────────────

    /// <summary>
    /// Resolves the <c>Command</c> DP on <paramref name="nodeId"/>, checks
    /// <see cref="System.Windows.Input.ICommand.CanExecute"/>, and invokes
    /// <see cref="System.Windows.Input.ICommand.Execute"/> (L0, M2-01).
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ExecuteCommandAsync(string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct);

    // ── M2-05: wpf_click ─────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the primary click action on the element identified by <paramref name="nodeId"/>
    /// via the UI Automation <see cref="System.Windows.Automation.Provider.IInvokeProvider"/>
    /// pattern (L1, M2-05).
    /// Automation must be enabled (<c>EnableAutomation=true</c> in options).
    /// When a <see cref="System.Windows.Input.ICommand"/> is bound via
    /// <c>ButtonBase.CommandProperty</c>, the response includes a
    /// <c>wpf_execute_command</c> suggestion (L0 is preferred for command-bound elements).
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ClickAsync(string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct);

    // ── M2-08: wpf_resolve_binding ────────────────────────────────────────────

    /// <summary>
    /// Returns the full binding chain for <paramref name="propertyName"/> on the element
    /// identified by <paramref name="nodeId"/>: path, source, intermediate values, converter,
    /// mode, validation errors, and overall status (M2-08).
    /// </summary>
    Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ResolveBindingAsync(string,string,CancellationToken)"/>.</summary>
    Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct);

    // ── M2-09: wpf_wait_for_property ─────────────────────────────────────────

    /// <summary>
    /// Polls <paramref name="propertyName"/> on the element identified by <paramref name="locator"/>
    /// until the observed value equals <paramref name="expectedValue"/> (when
    /// <paramref name="presenceExpected"/> is <c>"present"</c>) or until the element disappears
    /// (when <paramref name="presenceExpected"/> is <c>"absent"</c>), or until
    /// <paramref name="timeoutMs"/> elapses.
    ///
    /// Intelligent waiting: between polls the method waits for the
    /// <see cref="SnoopWPF.Agent.Engine.Sync.IdlingResourceRegistry"/> to signal idle so that
    /// CPU is not wasted spinning during heavy rendering or animation.
    ///
    /// Returns a <see cref="Dtos.WaitForPropertyResultDto"/> describing whether the condition
    /// was satisfied. On timeout, throws <see cref="SnoopException"/> with code
    /// <see cref="SnoopErrorCode.DispatcherBusy"/> and a suggestion to call
    /// <c>wpf_pump_until_idle</c> (M2-11).
    /// </summary>
    Task<Dtos.WaitForPropertyResultDto> WaitForPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string? expectedValue,
        int timeoutMs,
        string presenceExpected,
        CancellationToken ct);

    // ── M2-10: wpf_poll_changes ───────────────────────────────────────────────

    /// <summary>
    /// Returns the changeset (added / removed / property-mutated node IDs) that occurred
    /// since <paramref name="sinceVersion"/> and the current <c>treeVersion</c> to use
    /// as the baseline for the next call (M2-10).
    ///
    /// Non-blocking: returns immediately with the current snapshot delta.
    /// Callers that need to wait for a specific mutation should call
    /// <c>wpf_pump_until_idle</c> first (M2-11).
    /// </summary>
    /// <param name="sinceVersion">
    /// The tree version returned by a previous call. Pass 0 to receive all currently
    /// registered nodes as "added".
    /// </param>
    /// <param name="rootLocator">
    /// Optional locator that scopes the poll to a subtree.
    /// When null the entire registered node set is compared.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<Dtos.PollChangesResultDto> PollChangesAsync(
        long sinceVersion,
        WpfLocator? rootLocator,
        CancellationToken ct);
}
