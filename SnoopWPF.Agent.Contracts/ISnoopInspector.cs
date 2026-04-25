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
    /// <summary>Returns session-level information about the attached WPF process.</summary>
    Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct);

    /// <summary>Returns all top-level WPF windows; optionally includes hidden windows.</summary>
    Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct);

    /// <summary>Returns the visual (or logical) tree rooted at <paramref name="rootNodeId"/>, up to <paramref name="maxDepth"/> levels deep.</summary>
    Task<VisualTreeResultDto> GetVisualTreeAsync(
        string? rootNodeId,
        int maxDepth,
        string treeType,
        List<string>? includeProperties,
        CancellationToken ct);

    /// <summary>Returns a cursor-paginated page of direct children of the node identified by <paramref name="nodeId"/>.</summary>
    Task<CursorPage<NodeDto>> GetChildrenAsync(
        string? nodeId,
        string treeType,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Returns the ancestor chain of the node identified by <paramref name="nodeId"/>, up to <paramref name="maxLevels"/> levels.</summary>
    Task<List<AncestorDto>> GetAncestorsAsync(
        string nodeId,
        int? maxLevels,
        CancellationToken ct);

    /// <summary>Searches the visual/logical tree for elements matching the given type, name, and property conditions.</summary>
    Task<FindElementResultDto> FindElementsAsync(
        string? typeName,
        string? name,
        string? rootNodeId,
        List<PropertyConditionDto>? conditions,
        string treeType,
        int maxResults,
        CancellationToken ct);

    /// <summary>Returns a rich element summary for the node identified by <paramref name="nodeId"/>.</summary>
    Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct);

    /// <summary>Returns a cursor-paginated page of dependency properties for the node identified by <paramref name="nodeId"/>.</summary>
    Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Sets a named dependency property on the node identified by <paramref name="nodeId"/> and returns the resulting state delta.</summary>
    Task<StateDeltaDto> SetPropertyAsync(
        string nodeId,
        string propertyName,
        string value,
        CancellationToken ct);

    /// <summary>Returns binding metadata for a named property on the node identified by <paramref name="nodeId"/>.</summary>
    Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct);

    /// <summary>Runs diagnostic providers against the node identified by <paramref name="nodeId"/> and returns cursor-paginated results.</summary>
    Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(
        string? nodeId,
        List<string>? providers,
        string? minLevel,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Returns cursor-paginated resource dictionary entries visible from the node identified by <paramref name="nodeId"/>.</summary>
    Task<CursorPage<ResourceDto>> GetResourcesAsync(
        string? nodeId,
        string? resourceKey,
        string? cursor,
        int take,
        CancellationToken ct);

    /// <summary>Captures a PNG screenshot of the element identified by <paramref name="nodeId"/>, or the entire window when null.</summary>
    Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct);

    /// <summary>Returns all triggers (Style, ControlTemplate, DataTemplate, and Element) defined on the node identified by <paramref name="nodeId"/>.</summary>
    Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct);

    /// <summary>Returns all attached behaviors on the node identified by <paramref name="nodeId"/>.</summary>
    Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct);

    /// <summary>
    /// Returns the visible, enabled controls in the visual subtree rooted at
    /// <paramref name="rootNodeId"/> (or <see cref="System.Windows.Application.Current"/> when null) that an
    /// LLM can interact with. Filtered to actionable types (buttons, inputs, checkboxes, menu items,
    /// hyperlinks, list items, sliders, expanders, tabs); skips invisible / zero-size controls.
    /// </summary>
    Task<ActionablesResultDto> GetActionablesAsync(
        string? rootNodeId,
        int maxResults,
        CancellationToken ct);

    /// <summary>
    /// Executes an ordered list of L0/L1 mutation primitives (<c>click</c>, <c>double_click</c>,
    /// <c>execute_command</c>, <c>set_text</c>) in a single round-trip. When
    /// <paramref name="stopOnError"/> is <see langword="true"/> the sequence aborts at the first
    /// step that returns <c>StateDelta.Success=false</c>; otherwise all steps run and the result
    /// reports per-step outcome.
    /// </summary>
    Task<ActionSequenceResultDto> ExecuteActionSequenceAsync(
        List<ActionStepDto> steps,
        bool stopOnError,
        CancellationToken ct);

    /// <summary>
    /// Fires one mutation primitive and then polls a property predicate on the target node
    /// until the predicate is satisfied or <paramref name="timeoutMs"/> elapses. Replaces the
    /// "click + wait_for_property" round-trip pair common in dialog-driven LLM nav flows.
    /// </summary>
    Task<ActUntilResultDto> ActUntilAsync(
        ActionStepDto action,
        ActUntilPredicateDto predicate,
        int timeoutMs,
        CancellationToken ct);

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

    // ── M2-04a: wpf_select_item ──────────────────────────────────────────────

    /// <summary>
    /// Selects an item in an <c>ItemsControl</c>/<c>Selector</c> identified by
    /// <paramref name="nodeId"/> via <c>SetValue(Selector.SelectedIndexProperty, …)</c>
    /// (L0, M2-04a, non-virtualized path).
    ///
    /// <paramref name="identifier"/> accepts:
    /// <list type="bullet">
    ///   <item>Zero-based integer index (e.g. <c>"0"</c>).</item>
    ///   <item>Exact text — matched case-insensitively against each item's <c>ToString()</c>.</item>
    ///   <item>Partial text — unambiguous substring; ambiguous → <see cref="FailureReason.LocatorAmbiguous"/>.</item>
    /// </list>
    ///
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct);

    /// <summary>Locator overload for <see cref="SelectItemAsync(string,string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct);

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

    // ── M2-16: wpf_set_slider_value ───────────────────────────────────────────

    /// <summary>
    /// Sets the value of a <c>Slider</c> or any <c>RangeBase</c> element
    /// identified by <paramref name="nodeId"/> via <c>SetValue</c> (L0, M2-16).
    /// When <paramref name="normalized"/> is <see langword="true"/> the value is
    /// interpreted as a fraction in [0, 1] mapped to <c>Minimum + value × (Maximum − Minimum)</c>.
    /// </summary>
    Task<StateDeltaDto> SetSliderValueAsync(string nodeId, double value, bool normalized, CancellationToken ct);

    /// <summary>Locator overload for <see cref="SetSliderValueAsync(string,double,bool,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> SetSliderValueAsync(WpfLocator locator, double value, bool normalized, CancellationToken ct);

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

    // ── M2-06: wpf_toggle ────────────────────────────────────────────────────

    /// <summary>
    /// Flips the toggle state of the element identified by <paramref name="nodeId"/>
    /// via the UI Automation <see cref="System.Windows.Automation.Provider.IToggleProvider"/>
    /// pattern (L1, M2-06). Non-deterministic — always flips to the opposite state.
    /// <c>CheckBox</c> and <c>RadioButton</c> are rejected with
    /// <see cref="FailureReason.PatternNotSupported"/> and a <c>wpf_set_check_state</c>
    /// suggestion per PRD §5.2.
    /// Automation must be enabled (<c>EnableAutomation=true</c> in options).
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ToggleAsync(string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct);

    // ── M2-07: wpf_expand_collapse ───────────────────────────────────────────

    /// <summary>
    /// Expands or collapses the element identified by <paramref name="nodeId"/>
    /// via the UI Automation <see cref="System.Windows.Automation.Provider.IExpandCollapseProvider"/>
    /// pattern (L1, M2-07).
    /// <paramref name="action"/> must be <c>"expand"</c> or <c>"collapse"</c> (case-insensitive).
    /// Automation must be enabled (<c>EnableAutomation=true</c> in options).
    /// Returns a <see cref="StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct);

    /// <summary>Locator overload for <see cref="ExpandCollapseAsync(string,string,CancellationToken)"/>.</summary>
    Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct);

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

    // ── M2-11: wpf_pump_until_idle ────────────────────────────────────────────

    /// <summary>
    /// Blocks until the WPF dispatcher and all monitored idling resources are simultaneously
    /// idle, or until <paramref name="timeoutMs"/> elapses (PRD §5.4, §8.2, M2-11).
    /// </summary>
    Task<Dtos.PumpUntilIdleResultDto> PumpUntilIdleAsync(
        int timeoutMs,
        IReadOnlyList<string>? resources,
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

    // ── WS3-02: wpf_double_click ─────────────────────────────────────────────

    /// <summary>
    /// Fires a WPF routed double-click event on the element identified by
    /// <paramref name="nodeId"/> (L1, WS3-02). Primary path raises
    /// <c>MouseLeftButtonDown</c> + <c>MouseLeftButtonUp</c> twice with
    /// <c>ClickCount=2</c> on the second pair. When the primary path does
    /// not set <c>Handled=true</c> and the control is not a known-good type,
    /// a <c>SendInput</c> mouse fallback is used and a warning is emitted.
    /// Returns <see cref="Dtos.StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<Dtos.StateDeltaDto> DoubleClickAsync(string nodeId, CancellationToken ct);

    // ── WS3-03: wpf_select_item_by_scroll ────────────────────────────────────

    /// <summary>
    /// Scrolls the <c>ItemsControl</c> identified by <paramref name="nodeId"/>
    /// until the item at <paramref name="targetIndex"/> is realized, then
    /// selects it (WS3-03). Uses <c>BringIndexIntoView</c> to materialize
    /// virtualized containers before selecting.
    /// Returns <see cref="Dtos.StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<Dtos.StateDeltaDto> SelectItemByScrollAsync(string nodeId, int targetIndex, CancellationToken ct);

    // ── WS3-04: wpf_select_item_by_index ─────────────────────────────────────

    /// <summary>
    /// Selects the item at the given zero-based <paramref name="index"/> in the
    /// <c>Selector</c> identified by <paramref name="nodeId"/> by setting
    /// <c>Selector.SelectedIndex</c> (L0, WS3-04). Does not force
    /// materialization — use <c>wpf_select_item_by_scroll</c> for virtualized lists.
    /// Returns <see cref="Dtos.StateDeltaDto"/> describing the outcome.
    /// </summary>
    Task<Dtos.StateDeltaDto> SelectItemByIndexAsync(string nodeId, int index, CancellationToken ct);

    // ── WS3-06: wpf_get_list_items ────────────────────────────────────────────

    /// <summary>
    /// Enumerates the realized item containers in the <c>ItemsControl</c>
    /// identified by <paramref name="nodeId"/> and returns their index,
    /// node ID, display name, and selection state (WS3-06).
    /// Virtualized items that have not yet been materialized are omitted.
    /// </summary>
    Task<System.Collections.Generic.List<Dtos.ListItemDto>> GetListItemsAsync(string nodeId, CancellationToken ct);
}
