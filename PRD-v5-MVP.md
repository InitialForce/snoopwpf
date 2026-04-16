# PRD v5-MVP: SnoopWPF.Agent — Minimum Viable Shippable

> **Status**: Scope-frozen. 18 agent-visible tools + 4 utility, 3 milestones,
> polling-primary, all Wave-2 and Wave-3 critical findings applied inline.
> Target delivery: P50 18–22 weeks, P90 26–32 weeks for 1 engineer (revised
> from 11–17 per Wave-3 delivery-realism audit). Companion to
> `PRD-v5-ideal.md` (North Star architecture, 1,837 lines) — v5-MVP is what
> teams ship from; v5-ideal is what v2+ grows into.
>
> **Open scope decision** (not resolved in this revision): Wave-3 R-C
> proposed an alternate 10-tool / 7–9 week path that drops all L1 act tools,
> ancestors/children pagination, and screenshot. This doc pursues the
> honest-schedule 18-tool path. The aggressive-cut path remains a
> documented alternative if shipping sooner is the higher priority.
>
> **Codebase state** (as of this revision): BEAD-027 complete;
> post-BEAD-027 "review-fix wave" closed all 8 addressable §14 bugs
> and the security/threading findings from Waves 2+3. BEAD-028 (docs,
> security, log hardening) is the only remaining pre-existing bead.
> M1/M2 feature work unstarted. 11 agent projects including 3 test
> projects. Implementation is further along in hardening than the
> PRD's Wave-2 snapshot suggested; M1 feature scope unchanged.

---

## 0. Why this document exists

PRD v5-ideal covers 72 tools, 11 milestones, 176 user stories across five
new subsystems. Wave-2 review convergence (5 Opus reviewers + 10 Sonnet SOTA
research agents) established that this scope is 2.5–4 years of engineering
and 1.8× Cursor's hard tool-count cap. v5-MVP is the disciplined subset
that (a) validates the core architectural thesis, (b) solves the reference
consumer's (MotionCatalyst) concrete automation problem, and (c) wins the
market within one quarter.

Wave-3 review (4 fresh-eyes Opus reviewers with distinct lenses: red-team,
agent-consumer UX, YAGNI hardliner, delivery realism) surfaced 2 new
security findings, 4 new code bugs, 3 concurrency hazards, and concrete
spec gaps in the state-delta contract. All findings are applied in this
revision. Full synthesis in `PRD-v5-reviews-wave3.md`.

Everything in v5-ideal that is *not* in v5-MVP remains architecturally
committed for release 2.0+. Nothing is abandoned, only ordered.

---

## 1. Thesis (testable, not aspirational)

> For owned WPF applications, an in-process co-located MCP server exposing
> ~20 tools — split across inspection, semantically-honest input, bounded
> polling sync, and ViewModel-aware semantic navigation — outperforms
> cross-process UI-Automation approaches by 10–100× on latency and 2–5×
> on agent task-success rates. Headless parity is identical.

Success measured by §11 metrics, not by claim.

---

## 2. The five capabilities v5-MVP ships

1. **Co-located in-process inspection** — the 15 v3 tools, hardened, with
   all critical security findings applied. The moat: no cross-process UIA
   tool can match in-process `DependencyProperty`, `ICommand`, `Binding`,
   `DataContext` access.
2. **Honestly-named input** — 8 tools split across L0 (semantic shortcut)
   and L1 (AutomationPeer). No disguised `click`-means-`ExecuteCommand`.
   L3/L4 deferred to release 2.0 pending spike S-2 results.
3. **Normalized post-action state delta** — every mutation tool returns
   the canonical schema (§7). Eliminates VeriGUI's 72.3% repeat-on-
   unchanged-state agent-failure class.
4. **Polling-primary sync** — `wpf_wait_for_property`, `wpf_poll_changes`,
   `wpf_pump_until_idle`. Backed by the idle-resource AND-gate contract.
   SSE deferred pending MCP client support.
5. **Semantic navigation** — `wpf_resolve_binding`. Unique to in-process;
   nothing in the ecosystem offers this today. `wpf_trace_command` and
   `wpf_find_by_viewmodel` deferred to v1.1 after Wave-3 spot-check
   against MC code (`ReactiveCommand ??=` lazy instantiation defeats
   reverse-index; interface-typed DataContext defeats short-name match).

**Total: 18 tools.** Inside SOTA's empirical sweet spot, under Cursor's
40-tool hard cap, below context-bloat threshold.

---

## 3. Non-goals (explicit cuts from v5-ideal)

Each is an architectural commitment for v2.0+, not abandoned.

- L3 (`InputManager.ProcessInput`) — deferred pending spike S-2 closure.
- L4 (`SendInput` hardware input) — deferred; tiered-input opt-in later.
- Recording & replay — deferred to v2.0.
- Time-travel, causal lineage, state-machine induction — v2.0+ or dropped.
- Multi-agent coordination (driver / observer / arbiter) — v2.0+.
- Session federation — removed pending concrete cross-process consumer use case.
- Live edit mode (XAML injection) — v2.0 pending security spike on XAML allowlist.
- Visual diff (ΔE / SSIM / MobileCLIP) — v2.0; screenshots are captured but not
  diffed.
- Observability subsystem (OTEL / ETW / Chrome trace) — v2.0; basic
  `DiagnosticSource` events only in MVP.
- Virtual clock — v2.0. MVP assumes real time.
- Injection mode input (L3/L4 in injected processes) — v2.0.
- Shared-memory transport — v2.0.

Preserved from v3/v4: NuGet in-process mode, named-pipe transport for
out-of-process tooling, injection mode for read-only inspection of third-
party apps.

---

## 4. Architecture (compact)

### 4.1 Integration modes

Two modes ship in MVP:

| Mode | Hosts MCP | Transport | Max tier | Use |
|------|-----------|-----------|----------|-----|
| **Co-located** | Target WPF app | stdio | L1 | Owned apps. Primary. |
| **Injection** | `snoop-mcp.exe` external | stdio + pipe to target | L0 read-only | Third-party apps. Inspection only. |

NuGet in-process + named pipe is retained from v3 but unchanged.

### 4.2 Boot sequence (co-located)

```
MotionCatalyst.exe --mcp-stdio [--headless]
  1. Program.Main line 1: Console.SetOut(TextWriter.Null) — stdout takeover
     (FD held by SnoopAgent for MCP). Must precede CliFx.
  2. CliFx argument parse; detect --mcp-stdio.
  3. AsyncApp spawns GUI thread; WPF Application + Dispatcher created there.
  4. In _app.Dispatcher.BeginInvoke: SnoopAgent.StartCoLocated(options).
  5. Agent self-test: UnsafeAccessor resolution, HwndSource existence.
  6. MCP loop runs on a transport thread; every tool marshals to Dispatcher.
```

### 4.3 Session policy

Bound at session creation, not per-call:

- `Mode`: co-located | injection.
- `MaxTier`: L0 | L1 (MVP cap; L3/L4 deferred).
- `EnableAutomation`: bool; gates all L1 input.
- `EnableMutation`: bool; gates `wpf_set_property` and L0 semantic shortcuts.
- `EnableRedaction`: bool; gates output-side redaction. **Forced `true`
  in injection mode** regardless of caller option (MF-11). Caller
  choice respected in co-located mode.
- `RedactionPolicy`: keyword list + structural-sensitivity runtime-type
  map (MF-10) + `[Sensitive]` attribute recognition.
- `AllowSensitiveRetention`: bool; gates per-call `retainSensitive`
  opt-outs.
- Hints are per-call; ceilings are per-session and cannot be escalated.

### 4.4 Strategy layer

Two interfaces in `SnoopWPF.Agent.Contracts` (from v5-ideal, pre-split):

```csharp
public interface IDeterministicInputStrategy {
    DeterministicInputResult Invoke(DependencyObject target,
                                    InputIntent intent,
                                    CancellationToken ct);
}
// L3/L4 deferred; IProbabilisticInputStrategy defined but not implemented in MVP.
```

Selector lives in `SnoopWPF.Agent.Automation`, not `Input.*`, because it
reads session-policy.

### 4.5 Project structure (additive to existing 11 projects)

Existing 11 agent projects (8 production + 3 test):
`Contracts`, `Engine`, `Server`, `Tools`, `Remote`, `Injection`, `Host`,
`Cli`, `Tests`, `IntegrationTests`, `InjectionTests`.

New MVP projects:

- `SnoopWPF.Agent.Input.Deterministic` — L0/L1 strategies.
- `SnoopWPF.Agent.Query` — sync + semantic (merged per Reviewer A).
- `SnoopWPF.Agent.Automation` — top-level package + tool types.
- `SnoopWPF.Agent.Shim.FlaUI` — migration adapter for MotionCatalyst.

Existing 11 projects unchanged by MVP scope.
`SnoopWPF.Agent.Input.Probabilistic`, `Capture`, `Recording`,
`Observability`, `MultiAgent`, `LiveEdit` are deferred.

---

## 5. The 18-tool surface (Stagehand act/observe/extract)

### 5.1 Observe (9 tools — all from v3, refined per Wave-3)

| # | Tool | Purpose |
|---|------|---------|
| 1 | `wpf_get_session_info` | Mode, MaxTier, dispatchers, process identity, **and top-level windows inline with locators** (Wave-3 W3-C2 fix — enables 3-call bootstrap target). |
| 2 | `wpf_get_windows` | Refresh top-level window list. Specialization for agents that have already called `wpf_get_session_info` once. |
| 3 | `wpf_get_visual_tree` | Subtree dump, depth-capped |
| 4 | `wpf_get_children` | Cursor-paginated children |
| 5 | `wpf_get_ancestors` | Parent chain |
| 6 | `wpf_find_elements` | Query by type + automationId + name; returns locators. **Each result includes `hasCommandBinding: bool`** (Wave-3 W3-C2) so agents pick L0 `wpf_execute_command` vs L1 `wpf_click` without extra `inspect_element` round-trip. |
| 7 | `wpf_inspect_element` | Rich summary including `dataContextType` |
| 8 | `wpf_get_properties` | Cursor-paginated DP values, redacted per policy |
| 9 | `wpf_capture_screenshot` | `RenderTargetBitmap` or WGC, inline or blob-ref |

### 5.2 Act (8 tools)

Every Act tool description exposes four components per arxiv:2602.14878
(Wave-3 W3-E1): **Purpose**, **Guidelines** (prefer/over/when), **Limitations**
(what it does not handle), **Applies to** (typed exclusion list). The
tables below capture the normative component; full descriptions ship in
the MCP `tools/list` response.

| # | Tool | Tier | Purpose |
|---|------|------|---------|
| 10 | `wpf_execute_command` | **L0** | Resolve `ICommand` via DP binding, `CanExecute` gate, `Execute()`. Explicit shortcut. |
| 11 | `wpf_set_text_value` | **L0** | Set `TextBox.Text`/`PasswordBox.Password` via `SetValue`. Explicit shortcut. |
| 12 | `wpf_set_check_state` | **L0** | Set `IsChecked` deterministically to a target value. |
| 13 | `wpf_select_item` | **L0** | Set `IsSelected`/`SelectedItem`; supports virtualized lists with scroll + partial-text match (MC requirement). |
| 14 | `wpf_click` | **L1** | `UIElementAutomationPeer.GetPattern(Invoke).Invoke()`. Fallback only — prefer `wpf_execute_command` when `hasCommandBinding: true`. |
| 15 | `wpf_toggle` | **L1** | `IToggleProvider.Toggle()` — flips current state. |
| 16 | `wpf_expand_collapse` | **L1** | `IExpandCollapseProvider`. |
| 17 | `wpf_set_property` | **L0** | v3's mutation tool, gated by `EnableMutation`. Gate lives at strategy layer (finding C2 fixed). |

**Per-tool Guidelines / Limitations / Applies to** (Wave-3 W3-E1):

- **`wpf_execute_command`**
  - Guidelines: Prefer over `wpf_click` when `hasCommandBinding: true`
    in find_elements output — cheaper, deterministic, bypasses visual
    hit-testing.
  - Limitations: Only reaches `ICommand` bound via `Command` DP; does
    not fire routed UI events downstream.
  - Applies to: `Button`, `MenuItem`, `Hyperlink`, and any custom
    element exposing `Command`.
- **`wpf_set_text_value`**
  - Guidelines: Use for all programmatic text input in MVP.
    `wpf_type_text` is deferred to v2.0 (L3 required for IME/autocomplete).
  - Limitations: Does not trigger `TextChanged` via user-keystroke
    simulation; sets text atomically. Composition-IME scenarios not
    covered.
  - Applies to: `TextBox`, `PasswordBox`, `RichTextBox` (plaintext).
- **`wpf_set_check_state`**
  - Guidelines: Use when you need a specific final state
    (`checked`/`unchecked`/`indeterminate`). Prefer over `wpf_toggle`
    when target state is known.
  - Limitations: Does not invoke `Click`-routed events.
  - Applies to: `CheckBox`, `RadioButton`. **Not for** `ToggleButton`
    (use `wpf_toggle`).
- **`wpf_select_item`**
  - Guidelines: Use for any ItemsControl-based selection, including
    virtualized lists. Handles scroll-to-materialize internally.
  - Limitations: Partial-text match requires unambiguous substring;
    ambiguous returns `LOCATOR_AMBIGUOUS` failure.
  - Applies to: `ListBox`, `ListView`, `ComboBox`, `TreeView`,
    `DataGrid` rows, and any `Selector`-derived control.
- **`wpf_click`**
  - Guidelines: Use only when no `Command` binding exists, or when the
    semantic behaviour is coupled to `Click`-routed events that
    downstream code subscribes to. For `ICommand`-bound elements
    prefer `wpf_execute_command`.
  - Limitations: L1 only — uses AutomationPeer pattern; does not
    produce real mouse input. Agents needing hardware-level input wait
    for v2.0 L3/L4.
  - Applies to: Any `UIElement` with `UIElementAutomationPeer`.
- **`wpf_toggle`**
  - Guidelines: Flips current `IsChecked` state. Non-deterministic on
    outcome. Use `wpf_set_check_state` when target state matters.
  - Limitations: Returns element to the opposite state of whatever it
    is now.
  - Applies to: `ToggleButton`, `MenuItem` with `IsCheckable=true`.
    **Not for** `CheckBox`/`RadioButton` (use `wpf_set_check_state`).
- **`wpf_expand_collapse`**
  - Guidelines: Use for tree nodes, expanders, grouped list headers.
  - Limitations: `IExpandCollapseProvider` must be implemented by the
    element.
  - Applies to: `TreeViewItem`, `Expander`, `GroupItem`.
- **`wpf_set_property`**
  - Guidelines: Low-level; prefer higher-level tools when available.
    Use for DP state that has no dedicated setter tool.
  - Limitations: Requires `EnableMutation: true` in session policy.
  - Applies to: Any `DependencyObject`. Respects DP coercion and
    validation.

**L1 `wpf_type_text` is deferred**: requires L3 text-composition for IME/autocomplete fidelity. In MVP, `wpf_set_text_value` (L0) covers 90%+ of typing scenarios. Callers who need real keyboard simulation wait for v2.0.

### 5.3 Extract (1 tool — narrowed moat)

| # | Tool | Purpose |
|---|------|---------|
| 18 | `wpf_resolve_binding` | Full binding evaluation trace (path, source, intermediate values, converter, validation errors). |

**Deferred to v1.1** per Wave-3 W3-C3 spot-check of MC code:

- `wpf_find_by_viewmodel` — MC DataContext is predominantly
  interface-typed (`ISessionVM`, not `SessionVM`). PRD's "short-name
  match" assumed concrete types. Additionally, `wpf_find_by_viewmodel`
  on a virtualized session-list requires scroll-to-materialize cycles
  (items not in visual tree until scrolled). Defer pending a dedicated
  spike covering both (a) interface-typed resolution and (b)
  virtualized-list materialisation.
- `wpf_trace_command` — MC uses the `ReactiveCommand ??=` lazy pattern;
  commands are null until first bound, so reverse-index misses
  uninstantiated commands. Defer until MC pattern compatibility proven
  or pattern changes upstream.

`wpf_resolve_binding` survives: MC bindings to `IsSelected`,
`IsExpanded`, `Date`, `TakesCount` are clean DP-to-VM-property chains
and benchmark well.

### 5.4 Not in the 18 (but also shipped — utility, unnamed in count)

Utility tools that don't enter the 18 because they're meta-level, not
agent-facing actions:

- `wpf_pump_until_idle(timeoutMs, resources?)` — idle-gate primitive.
- `wpf_wait_for_property(locator, propertyName, expectedValue, timeoutMs?, presenceExpected?)`
  — flat. `presenceExpected: "present" | "absent"` (default `present`)
  handles modal-dismissal and negative-existence cases surfaced in
  Wave-3 W3-E2.
- `wpf_poll_changes(sinceVersion, rootLocator?)` — polling sync baseline.
- `wpf_fetch_blob(blobRef)` — out-of-band binary retrieval.

These are sync/transport primitives; tools 1–18 are the agent surface.
Total shipped: 22. The 20-cap applies to agent-visible primary tools per
SOTA research; we ship under it.

---

## 6. The `WpfLocator` type

Every tool that previously accepted `nodeId: string` also accepts
`locator: WpfLocator`. `nodeId` remains valid for compat but analyzer
`SWPF0010` warns on hard-coded nodeIds in consumer code.

```json
{ "$locator": "automationId=StartButton" }
{ "$locator": "type=Button, name=Start" }
{ "$locator": "path=Window\\Grid\\StackPanel\\Button" }
{ "$locator": "viewModel=SessionVm, property=IsSelected, value=true" }
```

Re-resolves on every tool call. Zero stale-handle failures under tree
mutation, virtualization, navigation.

**Locator stability ordering** (Wave-3 W3-H3):

- `automationId=` — strongest. Stable across tree mutation and
  sibling reordering. **Preferred.**
- `viewModel=` — strong. Stable as long as VM-property binding exists.
- `type=, name=` — stable when `Name` is set; weak if omitted.
- `path=` — **NOT durable under sibling reordering**. Three sibling
  `Button`s in a `StackPanel` all match the same `path=` locator;
  `FindByPathSegments` returns the first. Use only when nothing better
  exists; analyzer `SWPF0011` warns on stored `path=` locators in
  consumer code.

`wpf_find_elements` returns both `nodeId` (immediate) and `locator`
(re-resolving); consumers should store locators, prefer
`automationId=` or `viewModel=` forms, and not cache `path=` results.

**Resolution cost bounds** (Wave-3 W3-E2, bug #9): each locator
resolution is capped at registering ≤100 new `NodeRegistry` entries.
Beyond the cap, the call returns `LOCATOR_AMBIGUOUS` and the agent is
required to narrow the query. This prevents unbounded NodeRegistry
growth on wide `viewModel=` resolutions against large virtualized
lists.

---

## 7. Canonical post-action state delta

Every mutation tool (act family + `wpf_set_property`) returns this shape
uniformly. Addresses VeriGUI's 72.3% repeat-on-unchanged-state failure.

### 7.1 Default response (success)

```json
{
  "success": true,
  "elementVisible": true,
  "stateChanged": true,
  "currentFocus": { "$locator": "automationId=TextBox1" },
  "treeVersionDelta": 2,
  "failureReason": null,
  "suggestion": null
}
```

Per Wave-3 W3-C1: `treeVersionBefore`, `treeVersionAfter`, `elapsedMs`,
`chosenTier`, `actionabilityChecksFailed` are suppressed on success to
minimize per-call token cost. Enable via `debug: true` call hint.

### 7.2 Default response (failure)

```json
{
  "success": false,
  "elementVisible": false,
  "stateChanged": false,
  "currentFocus": null,
  "treeVersionDelta": 0,
  "actionabilityChecksFailed": ["visible"],
  "failureReason": "ELEMENT_NOT_VISIBLE",
  "suggestion": {
    "tool": "wpf_wait_for_property",
    "args": {
      "locator": { "$locator": "automationId=SaveButton" },
      "propertyName": "IsVisible",
      "expectedValue": true,
      "timeoutMs": 5000
    }
  }
}
```

`actionabilityChecksFailed` is populated only on failure and lists only
the checks that failed.

### 7.3 `stateChanged` computation rule (Wave-3 W3-C1)

`stateChanged` is computed at **response-serialization time**, not at
`SetValue`-call time, by comparing the element's current observable
state against `previousValue` captured pre-call. This closes the
re-entrant-`PropertyChangedCallback` false-positive: a DP metadata
callback that synchronously reverts the value will produce
`stateChanged: false` despite the bump counter firing.

`previousValue` includes: for Act tools, the property the tool sets
(e.g. `IsChecked` for `wpf_set_check_state`); for `wpf_execute_command`,
the element's routed-event firing count + window-count delta (because
commands may not mutate their host element).

### 7.4 `failureReason` enum (Wave-3 W3-C1, W3-E2)

Exactly these 12 values. Tool descriptions include per-value suggestion
semantics so the agent re-invokes correctly.

| Value | Trigger | `suggestion.tool` |
|-------|---------|-------------------|
| `ELEMENT_NOT_FOUND` | Locator resolves to zero elements | `wpf_find_elements` |
| `ELEMENT_NOT_VISIBLE` | Element exists but `IsVisible=false` | `wpf_wait_for_property(IsVisible, true)` |
| `ELEMENT_NOT_ENABLED` | Element exists but `IsEnabled=false` | `wpf_inspect_element` (find why) |
| `CANNOT_EXECUTE_COMMAND` | `ICommand.CanExecute == false` | `wpf_resolve_binding` (find gating property) |
| `AUTOMATION_DISABLED` | Session `EnableAutomation: false` | `null` (session reconfig required) |
| `MUTATION_DISABLED` | Session `EnableMutation: false` | `null` (session reconfig required) |
| `TIER_MISMATCH` | Required tier > session `MaxTier`, OR no AutomationPeer / IInvokeProvider | `wpf_execute_command` (fallback to L0 if available) |
| `STATE_UNCHANGED` | Post-condition check: `success=true, stateChanged=false, treeVersionDelta=0` | `wpf_inspect_element` (verify target state) |
| `LOCATOR_AMBIGUOUS` | Locator matches >1 element, or resolution would exceed NodeRegistry cap | `wpf_find_elements` with `returnAll: true` |
| `DISPATCHER_BUSY` | Dispatcher couldn't acquire frame within budget | `wpf_pump_until_idle` |
| `ELEMENT_OUTSIDE_VIEWPORT` | Action requires visibility but target is scrolled out | `wpf_select_item` on parent list, or `null` if no scrollable parent |
| `PATTERN_NOT_SUPPORTED` | Element does not support the AutomationPeer pattern the tool requires | `wpf_get_properties` (inspect supported patterns) |

### 7.5 `suggestion` schema

```
type Suggestion =
  | { tool: string, args: object }  // machine-executable; agent invokes directly
  | null;                            // escalate to caller; no auto-remediation
```

Prose-form suggestions are not permitted. When no remediation exists
(`AUTOMATION_DISABLED`, `MUTATION_DISABLED`), `suggestion` is `null` and
the agent surfaces the failure.

### 7.6 Critical signal

`stateChanged == false && treeVersionDelta == 0 && success == true`
means "action completed but produced no visible effect" — agent must
not retry the same action; response returns
`failureReason: STATE_UNCHANGED` with a re-framed suggestion.

---

## 8. Synchronization model

### 8.1 Polling-primary

- `wpf_wait_for_property(locator, propertyName, expectedValue, timeoutMs?)`
  — flat parameters, agent-friendly, 90% of cases.
- `wpf_poll_changes(sinceVersion, rootLocator?)` — returns immediately;
  agents implement polling loops.
- `wpf_pump_until_idle(timeoutMs, resources?)` — idle-resource AND-gate
  blocking primitive.

SSE subscriptions are **experimental** and documented as "not supported
by any MCP client as of April 2026." Design does not commit to them;
added when client support ships.

### 8.2 Idle-resource AND-gate (Detox pattern)

```csharp
public interface IIdlingResource {
    string Name { get; }
    bool IsIdle { get; }
    event EventHandler IdleChanged;
}
```

Built-in resources registered at agent start. Each specifies the
Dispatcher priority at which its idle check is evaluated (Wave-3 W3-H1).
Tool-dispatch operations run at `DispatcherPriority.Send` (highest);
idle checks must run at a priority **above `ApplicationIdle`** so they
are not starved by concurrent tool dispatch.

- `DispatcherIdlingResource` — idle check at `DispatcherPriority.ContextIdle`.
  (Was `ApplicationIdle`; raised per W3-H1 to avoid `Send`-priority
  starvation under three concurrent tool calls + animation load.)
- `CompositionRenderingResource` — idle when `CompositionTarget.Rendering`
  fires with no pending changes for one full frame. Check runs at
  `ContextIdle`.
- `StoryboardResource` — per-active-Storyboard via
  `Timeline.CurrentStateInvalidated`. State read at `ContextIdle`.
- `DispatcherTimerResource` — idle when no pending `DispatcherTimer` is
  active. State read at `ContextIdle`.
- `CustomAppResource` — `SnoopAgentOptions.IdlingResources` extension
  point. Must declare check priority; default `ContextIdle`.

`wpf_pump_until_idle` proceeds only when **all** resources are
simultaneously idle. Throws `AnimationRunawayException` after
configurable ceiling (default 5 s, Flutter `pumpAndSettle` parity).

**Concurrency invariant**: a tool handler holding the concurrency
semaphore at `DispatcherPriority.Send` must not call
`wpf_pump_until_idle` recursively. `InputStrategySelector` enforces
this by rejecting nested pumps with `DISPATCHER_BUSY`.

### 8.3 Tree versioning (multi-source)

Per Wave-1 WPF-agent: `VisualDiagnostics.VisualTreeChanged` is
debugger-only and not usable. MVP tracker uses:

1. Per-registered-node `FrameworkElement.Loaded` / `Unloaded`.
2. `Panel.Children.CollectionChanged` on registered panels.
3. `CompositionTarget.Rendering` frame-tick walk, budget-capped at 200 µs.
4. Explicit `NodeRegistry.Bump()` for mutations the detector can't see.

Watched DPs tracked via `ValueChangedEventManager` weak-event pattern
(leak-free, per Wave-1 WPF agent).

---

## 9. Security (all Wave-2 + Wave-3 critical findings applied)

### 9.1 Strategy-layer gating (MF fix for v4 C2)

`EnableAutomation` / `EnableMutation` checks live in `InputStrategySelector`,
not tool handlers. Any path to a strategy — `wpf_click`, `wpf_set_property`
with tier hint, `wpf_execute_command` — passes the same gate.

### 9.2 Structural redaction (v4 H1 + Wave-3 MF-10)

- Keyword redaction (from v3) on DP names.
- `wpf_type_text`-equivalent (`wpf_set_text_value`) text parameter treated
  `SensitiveText` by default; opt-out via `advanced.retainSensitive: true`
  requires `AllowSensitiveRetention: true` at session policy.
- **MF-10 `ToString()` override bypass** (Wave-3 R-A #4): property-name
  keyword matching is not sufficient. `DtoProjection.ToPropertyDto` reads
  `prop.Value?.ToString()`; any VM property whose type overrides
  `ToString()` to embed credentials (e.g. a `ConnectionStringBuilder`-
  shaped wrapper on a DP named `DatabaseConfig`) leaks in full.
  `Redact()` must inspect the **runtime type** of `prop.Value` against a
  structural-sensitivity map before invoking `ToString()`. Minimum map
  for MVP: `SecureString`, `NetworkCredential`, types deriving from
  `System.Data.Common.DbConnectionStringBuilder`, any type marked
  `[Sensitive]` (new attribute shipped in `SnoopWPF.Agent.Contracts`).
  On match, substitute the redaction sentinel regardless of property
  name.

### 9.3 `find_by_viewmodel` exfiltration prevention (v4 H2, Wave-2 MF-4)

`propertyPath` checked against redaction keyword list before resolution.
**Additionally**: when any DP on the target VM type matches a keyword,
the entire result returns `ViewModelRedacted`. Closes cross-property
correlation channel (Wave-2 MF-4).

### 9.4 Structured audit log (v4 H3 + Wave-3 bug #10)

JSONL at `%LOCALAPPDATA%\SnoopWPF\audit\{session}.jsonl`, owner-only ACL,
monotonic sequence, per-entry HMAC. `reason` field sanitized (newlines
stripped, cap 256 chars). Not stderr.

**HMAC chain specification** (Wave-3 bug #10, was underspecified):

- Writer: single dedicated background thread consuming a
  `Channel<AuditEntry>`. MCP transport thread and mutation callbacks
  produce to the channel; the writer is the sole HMAC state holder.
  Concurrent writes on the transport thread are serialized at the
  channel boundary, not at the filesystem.
- Algorithm: HMAC-SHA256 over
  `entryJson || prevHmac || sessionKey || counterNonce`. Chain-based —
  each entry's HMAC includes the previous entry's HMAC as tamper-
  evidence. Initial entry uses a fixed sentinel `prevHmac`.
- Session key derivation: 32 bytes from
  `System.Security.Cryptography.RandomNumberGenerator` at session start.
  Key is session-scoped, held in process memory only, discarded at
  session end. Not derived from process identity (prevents replay
  against a restarted process).
- Counter nonce: monotonic 64-bit integer incremented per entry,
  included to prevent splicing within a session.
- Verification: `snoop-audit-verify` tool (post-MVP) reads the JSONL
  and the session key file and recomputes the chain.

### 9.5 Stdout takeover (Wave-2 analysis, Reviewer B critical bug)

`SnoopAgent.StartCoLocated` first operation: `Console.SetOut(TextWriter
.Null)`. Roslyn analyzer `SWPF0001` fires on `Console.Write*` in consumer
projects marked `[SnoopMcpEntrypoint]`. Existing snoopwpf bugs (SnoopAgent
.cs:80, McpServerSetup.cs:84) fixed inline.

### 9.6 Predicate DSL hardening (Wave-2 MF-5)

- `$version: 1` required.
- Predicate JSON max 16 KB.
- Resolved-value cap 64 KB.
- Regex match-length cap 4 KB (separate from value cap).
- Amortized CPU budget 1 ms/s per subscription.
- Nesting depth 16, node count 256.
- `NonBacktracking` regex.

### 9.7 Injection-mode hard gates (v4 H4 + Wave-3 MF-11)

`InputStrategySelector` refuses to construct L0/L1 strategies in
injection mode in MVP (injection is inspection-only). L3/L4 never
available in injection mode; deferred to v2.0.

**MF-11 unconditional redaction** (Wave-3 R-A #8): session policy in
injection mode forces `EnableRedaction = true` regardless of caller
`SnoopAgentOptions`. The gate lives at session construction
(`SessionPolicy.Create(Injection, opts)` overrides the redaction flag
before returning), not as a tool-handler check. Rationale: an agent
injected into a third-party app (password manager, etc.) must never
expose unredacted DP values via the 9 observe tools, even if the
invoker forgot to configure redaction. Co-located mode retains the
caller's redaction choice because the caller owns the app.

### 9.8 `DOTNET_STARTUP_HOOKS` verification (Wave-2 MF-8)

When loaded via `DOTNET_STARTUP_HOOKS`, agent verifies its own assembly's
strong-name before activating. Documentation notes the env var must be
set via trusted config, not user-writable `.env` files.

### 9.9 Process identity for any future replay (Wave-2 v4 M1)

When recording ships in v2.0, cross-process replay identity is PID +
`GetProcessTimes` start time + executable SHA-256. MVP doesn't ship
recording so this is deferred with the rest of §10.

### 9.10 XAML sandbox (Wave-2 MF-1)

Live edit is deferred to v2.0. When it ships, explicit allowlist required:
`Grid`, `StackPanel`, `Border`, `TextBlock`, `Rectangle`, `Ellipse`, `Path`,
`Image`, `Canvas` only. **Prohibited**: `ObjectDataProvider`, `XmlDataProvider`,
`x:Static` on non-enum types, `x:Code`, `EventSetter`, any type from
`System.Diagnostics`, `System.IO`, `System.Reflection`.

---

## 10. Milestones

### M0 — Spikes (1–2 weeks, blocks only M1 items that depend on them)

Wave-3 R-D audit changed the spike priority ordering. The existing
snoopwpf codebase (~45 KLOC across 8 projects, working stdio integration-
test harness) has already de-risked S-1 and much of S-2. S-3 is the one
spike with genuine binary outcome.

- **S-1** — Co-located stdio round-trip. Already passed by existing
  `IntegrationTestFixture.cs` + `TestWpfApp.cs` harness. Re-verify as
  sanity check only (~1 day).
- **S-2** — `UnsafeAccessor` compilation under net6/8/9. Academic in
  MVP (L3/L4 deferred). ~1 day to verify accessors compile; detailed
  L3 validation deferred with L3.
- **S-3** (critical path, only real binary-outcome spike) — benchmark
  tree-change detection on the actual snoopwpf `NodeRegistry` against a
  200-node tree with 1e6 mutation cycles. Target: < 1 µs per mutation
  bump. If `Panel.Children.CollectionChanged` synchronous firing costs
  3–5 µs, the four-source feed needs a coalescing ring buffer — a new
  subsystem. ~3–5 days. **Know this result before writing any M1
  code.**
- **S-3b** (new, Wave-3 W3-H2) — single test proves mutation →
  `wpf_poll_changes` response without using `wpf_wait_for_property`.
  Uses `Thread.Sleep` + `wpf_poll_changes` only, measures expected
  tree-version delta. Closes the circular test-dependency hazard.
  ~1 day.
- **S-5** — `ValueChangedEventManager` weak-event leak test over 1e6
  bump cycles. ~2 days.
- (S-4 virtual-clock audit deferred with the rest of virtual clock.)

**Pre-M1 de-risk tasks** (Wave-3 W3-D3, additionally required before M1
code starts):

- **PR-1 Coverage-gap audit**: map each of the 18 active MC SpecFlow
  scenarios against the §12.3 30% gap items. Decide per scenario: shim
  covers / FlaUI stays / descoped. If 5+ scenarios need FlaUI fallback,
  rewrite M2 gate definition now. ~2 days.
- **PR-2 Headless CI wiring**: run existing `IntegrationTestFixture.cs`
  on GitHub Actions matrix (net6/8, x64) end-to-end before M1 code.
  ~2–3 days, pays back throughout M1+M2.

**Exit criteria**: each spike result written to `SPIKE-RESULTS.md` in
snoopwpf repo. Pre-M1 audits complete. M1 beads not opened until every
spike's result is either green or has a fallback plan.

### M1 — Foundation + hardening (3.5–4.5 weeks)

- Bugs 9 and 10 in §14 land as acceptance criteria on the beads that
  introduce `WpfLocator` and the audit-log writer respectively. Bugs
  1–8 are already closed — see §14.
- Add session policy fields to `SnoopAgentOptions`, including the
  MF-11 injection-mode `EnableRedaction` override.
- Add `SnoopWPF.Agent.Input.Deterministic` project.
- Add `IDeterministicInputStrategy`, `InputStrategySelector`.
- Add `Console.Out` takeover + analyzer `SWPF0001`.
- Add analyzer `SWPF0010` (raw `nodeId` storage warning) + `SWPF0011`
  (stored `path=` locator warning — Wave-3 W3-H3).
- Add `UnsafeAccessor` startup self-test (for future L3 readiness).
- Add ensure-`HwndSource` on startup for headless mode.
- Add `WpfLocator` type with NodeRegistry entry-creation cap per
  resolution (§6, bug #9).
- Add `wpf_find_elements` returning locators alongside nodeIds and
  `hasCommandBinding` (W3-C2).
- Expand `wpf_get_session_info` to include top-level windows inline
  (W3-C2).
- Add canonical post-action state-delta schema per §7 across v3
  mutation tools (`wpf_set_property` only in MVP). Includes
  `failureReason` enum, `suggestion: {tool, args}` schema,
  `stateChanged` response-serialization-time computation.
- Add `IIdlingResource` contract + built-in resources at `ContextIdle`
  priority (§8.2).
- Add `[Sensitive]` attribute + runtime-type redaction map (MF-10).
- Add HMAC audit-log writer on a `Channel<AuditEntry>` worker (§9.4
  bug #10).
- Add tool descriptions with Guidelines + Limitations + Applies-to
  (W3-E1) for every M1-touched tool.

**Gate**: all v3 integration tests still green; sample app + live
end-to-end stdio test of every v3 tool; S-3b circular-dependency test
green.

### M2 — Act + Extract + Sync + Integration (6–8 weeks, revised up per W3-D1)

- Add L0 act tools: `wpf_execute_command`, `wpf_set_text_value`,
  `wpf_set_check_state`, `wpf_select_item`.
- Add L1 act tools: `wpf_click`, `wpf_toggle`, `wpf_expand_collapse`.
- Add extract tool: `wpf_resolve_binding`. (`wpf_trace_command` dropped,
  `wpf_find_by_viewmodel` deferred to v1.1 per W3-C3.)
- Add sync utility tools: `wpf_wait_for_property` (with
  `presenceExpected` per W3-E2), `wpf_poll_changes`,
  `wpf_pump_until_idle`.
- Add `SnoopWPF.Agent.Shim.FlaUI` adapter.
- Close the 30% coverage gap per the PR-1 audit outcome (either in
  shim, or documented FlaUI retention for specific scenarios).
- VeriGUI-style 100-scenario test harness (measures repeat-on-
  unchanged-state rate; new build-out, not inherited).
- CI dual-stack wiring finalized (builds on PR-2).
- NuGet packaging + signing + feed decision for `SnoopWPF.Agent.Shim
  .FlaUI` + consumer `.mcp.json` update in MC.
- MotionCatalyst integration (see §12).

MC integration is **not** parallel with the rest of M2 work on a
1-engineer team per W3-D1; these tracks serialize.

**Gate**: MotionCatalyst's 18 active SpecFlow scenarios pass headlessly
through MCP tools with p95 < 10 ms per call. Zero `Task.Delay` in
scenario scripts. Repeat-on-unchanged-state agent failure rate < 5%
measured via VeriGUI-style test harness. All M2 user stories (including
VERIGUI-HARNESS, CI-DUAL-STACK, NUGET-PACKAGING, COVERAGE-GAP-AUDIT)
closed.

---

## 11. Success metrics

1. Claude Code, given only `wpf_get_session_info` (which returns windows
   inline per §5.1 W3-C2 fix), navigates to any named element in
   MotionCatalyst's main window and completes one act call within
   3 tool calls total, p95. The canonical chain:
   `get_session_info → find_elements → execute_command|click`.
   `find_elements` returning `hasCommandBinding` lets the agent pick
   L0 vs L1 without a 4th inspect call.
2. All 18 active MotionCatalyst SpecFlow scenarios pass headlessly with
   p95 per-tool-call < 10 ms and zero `Task.Delay` in scripts.
3. Agent repeat-on-unchanged-state failure rate < 5% over a 100-scenario
   run (baseline: VeriGUI 72.3% industry average).
4. `wpf_resolve_binding` correctly resolves the binding chain (path,
   source, value, converter, validation) for every interactive element
   in MotionCatalyst's session-list within one tool call, p99.
   (`wpf_find_by_viewmodel` deferred to v1.1 pending interface-typed
   DataContext spike per §5.3.)
5. Zero confirmed secret-in-property-dump incidents in a 1,000-property
   read corpus from MotionCatalyst live state.

CI gate: regression in any metric fails the build.

---

## 12. MotionCatalyst integration (consumer)

Non-normative; captures Wave-2 Reviewer C findings.

### 12.1 Boot-sequence changes

- `Program.cs` line 1: `Console.SetOut(TextWriter.Null)` when `--mcp-stdio`
  present.
- `AsyncApp.Start()` line ~134: `_app.Dispatcher.BeginInvoke(() =>
  SnoopAgent.StartCoLocated(...))` after ReactiveUI init, before
  `_taskCompletionSource.SetResult`.
- `.mcp.json`: command changes from `dotnet run McpFlaUIHelper` to
  `MotionCatalyst.exe --mcp-stdio --headless`.

### 12.2 Console.Write* remediation

Fix inline or redirect: `AnalysisView.xaml.cs:74`, `BertecSdk.cs:60`.
Firebase Analytics and CliFx are confirmed safe.

### 12.3 FlaUI migration

`SnoopWPF.Agent.Shim.FlaUI` (~400–600 LOC) implements
`IUIActionCatalog` used by SpecFlow steps. Step-defs unchanged;
implementation behind `SafeClick` etc. swaps to MCP tool calls.

**70% scenario coverage** at M2 close. Remaining 30% gaps:
- Slider value setter with range normalization.
- Virtualized-list scroll + partial-text match.
- Branding-baseline management in CI.
- `ResetToHome` 20-iteration state-machine reset.
- License-dialog detection in launch flow.
- Hardware-device-readiness gates.
- Multi-product launch param (`-p SwingCatalyst`).
- `--qa-mode` software rendering (must not conflict with any future
  WGC capture path).

These gaps addressed either in a follow-on MVP patch or by keeping
FlaUI for the specific scenarios that need them.

### 12.4 CI dual-stack

Runs FlaUI suite and Snoop suite in separate jobs. Diff-based equivalence
gate deferred to post-MVP; acceptance metric is "both pass" not "outputs
identical."

### 12.5 MCP wrapper decomposition

~1,900 LOC deleteable at M2 (`ElementOperations.cs`, `ElementTools.cs`,
`PropertyTools.cs`, `SelectorHelper.cs`). Retain: launch infrastructure
(`TestContextManager.cs` partial), `AppLifecycleTools.cs`, `CliTools.cs`,
crash detection.

---

## 13. User stories (24, one per tool group + 4 Wave-3 additions)

Each story has acceptance criteria that map to §11 metrics.

**Observe (5 stories)** US-MVP-001 through US-MVP-005 — one per existing
v3 tool that gains post-action-delta + locator support.

**Act (4 stories)** US-MVP-010 through US-MVP-013 — L0 act tools.
US-MVP-014 through US-MVP-016 — L1 act tools.

**Extract (1 story)** US-MVP-020 — `wpf_resolve_binding`.
(Was 3 stories for 3 tools; 2 deferred to v1.1 per §5.3.)

**Sync (3 stories)** US-MVP-030 through US-MVP-032 — polling primitives
+ idle-resource AND-gate.

**Session policy (1 story)** US-MVP-040 — `SnoopAgentOptions` extensions,
including MF-11 injection-mode redaction override.

**Consumer integration (3 stories)** US-MVP-050 through US-MVP-052 —
MotionCatalyst boot, Console takeover, shim adapter.

**Security (1 story)** US-MVP-060 — strategy-layer gating, structural
redaction (including MF-10 runtime-type map), audit log (bug #10 HMAC
chain), Console.WriteLine bug fixes.

**Wave-3 delivery hardening (4 stories)**:

- **US-MVP-070 VERIGUI-HARNESS** — build the 100-scenario test harness
  measuring repeat-on-unchanged-state rate. Required by §11 metric #3.
- **US-MVP-071 CI-DUAL-STACK** — GitHub Actions matrix net6/8, x64,
  running FlaUI and Snoop suites in separate jobs with "both pass"
  acceptance gate. Shipped in M0 pre-M1 phase per W3-D3.
- **US-MVP-072 NUGET-PACKAGING** — signing, feed selection (GitHub
  Packages vs nuget.org), `.mcp.json` update flow for consumers.
- **US-MVP-073 COVERAGE-GAP-AUDIT** — per-scenario mapping of §12.3
  30% gap items; decisions logged in `SPIKE-RESULTS.md`. Pre-M1 gate
  per W3-D3.

Full acceptance criteria deferred to `BEADS-MVP.md` during M0 planning.

---

## 14. Code bugs (historical record + M1 remaining)

Wave-2 and Wave-3 review surfaced 10 bugs. Bugs 1–8 were closed by the
post-BEAD-027 "review-fix wave"; bugs 9–10 cannot land until the
feature they attach to is built, and are kept here as specs for those
M1 items.

**Closed in the review-fix wave (bugs 1–8):**

1. `SnoopWPF.Agent.Server/SnoopAgent.cs:80` — `Console.WriteLine` on
   stdio path. **Fixed**: redirected to `Trace.TraceInformation`.
2. `SnoopWPF.Agent.Server/McpServerSetup.cs:84` — same. **Fixed**.
3. `SnoopWPF.Agent.Engine/Infrastructure/NodeRegistry.cs:75-84` —
   concurrent-registration race. **Fixed** (commits `6b7f196`, `c800b97`):
   `Add()` on `ConditionalWeakTable` guarded by try/catch on
   `ArgumentException`, re-reads the winner on the raced path.
4. `SnoopWPF.Agent.Engine/Infrastructure/CursorManager.cs:113-117` —
   consumed-cursor indistinguishable from not-found. **Fixed**
   (`c800b97`): `GetPage` distinguishes via `stale: false` for
   not-found, entry removal only when `!hasMore` under
   `lock (entry.SyncLock)`.
5. `SnoopWPF.Agent.Engine/Infrastructure/RedactionFilter.cs:61-65` —
   dead `PasswordBox.Password` type check. **Fixed** (`2aa3761`):
   branch removed; keyword-scan handles it. Comment at line 9 documents
   the reasoning.
6. `SnoopWPF.Agent.Engine/SnoopInspector.cs:1370-1373` — error-code
   ordering. **Fixed** (`ae1e9c8`):
   `ct.ThrowIfCancellationRequested()` runs before `DispatcherBusy`
   and `OperationTimedOut` in `RunOnDispatcherAsync`.
7. `CursorManager.GetPage` non-atomic `Offset` RMW race. **Fixed**
   (`c800b97`): `CursorEntry.SyncLock` added; all `Offset`
   read-modify-write under `lock (entry.SyncLock)`.
8. `NodeRegistry.cs` `Clear()` counter reset non-atomic. **Fixed**
   (`c800b97`): `Interlocked.Exchange(ref counter, 0)`. The
   `#if NET6_0_OR_GREATER` guard on `ClearForwardTable()` is
   intentional per in-file comment — behaviour differs correctly
   between frameworks, not a bug.

**Remaining — specs for M1 items that haven't landed yet:**

9. `WpfLocator` re-resolution NodeRegistry growth cap (§6, Wave-3
   R-A #9): cap new entries per locator-resolution call (default 100);
   sweep timer must take the same lock as `GetOrCreateId` or switch to
   a lock-free CAS. `WpfLocator` is not yet built — this is the
   acceptance criterion for the bead that introduces it.
10. Audit-log writer concurrency + HMAC chain (§9.4, Wave-3 R-A #10):
    single `Channel<AuditEntry>`-funneled writer; HMAC-SHA256 keyed
    with session key + counter nonce; key from
    `System.Security.Cryptography.RandomNumberGenerator` at session
    start. Audit log is not yet implemented — this is the acceptance
    criterion for the bead that introduces it.

The M1 bug-fix workload is therefore substantially reduced from the
original estimate: only items 9 and 10 remain, and both land alongside
their parent features.

---

## 15. Timeline

Revised per Wave-3 delivery-realism audit (W3-D1, W3-D2, W3-C4). The
"parallel M2 + MC integration" framing of the original estimate is a
1.5-engineer fiction for a 1-engineer team; tracks serialize.

- **M0 spikes + pre-M1 de-risk** (S-3, S-3b, S-5 + coverage-gap audit +
  CI wiring): 1–2 weeks.
- **M1 foundation**: 3.5–4.5 weeks (bugs 1–8 already closed in the
  review-fix wave; scope now covers locator NodeRegistry cap,
  state-delta schema, MF-10/MF-11, HMAC writer, tool-description
  expansion, idle-resource contract, analyzers).
- **M2 ship**: 6–8 weeks (bumped for VeriGUI harness build-out, CI
  dual-stack wiring, NuGet packaging, coverage-gap closure, idle-gate
  tuning).
- **MotionCatalyst integration** (consumer work, serial with M2):
  5–7 weeks (bumped for the §12.3 30% gap realities per W3-D1).

**P50 total: 18–22 weeks. P90 total: 26–32 weeks.** Polish,
documentation, open-source release: +4–6 weeks.

Historical: the original 11–17 week estimate assumed (a) spikes all
green no rework, (b) MC integration parallelizes, (c) idle-gate tunes
cleanly, (d) CI dual-stack is trivial, (e) no bug tail between
feature-complete and gate-passing. Any one of these slipping is
recoverable; the compounding is what moves the estimate to 18–22.

**If the 11–17 week target is fixed**: R-C's 10-tool aggressive-cut
path (drop all L1 act tools, ancestors/children/screenshot, keep only
L0 act + observe + one extract) is documented as the alternative
scope. That path is genuinely 7–9 weeks, and the cuts have MC-scenario
evidence. Keeping the 18-tool surface requires accepting the 18–22
week P50.

---

## 16. What comes after MVP

v1.1 (fast-follow):

- `wpf_find_by_viewmodel` — pending spike on interface-typed DataContext
  resolution + virtualized-list scroll-to-materialize (W3-C3).
- `wpf_trace_command` — pending compatibility work with lazy
  `ReactiveCommand ??=` pattern, or acceptance of limitation documented
  per-consumer (W3-C3).
- Full tool-description Guidelines/Limitations/Applies-to audit across
  all 22 tools (M2 ships these for new tools only; v3 tools get the
  expansion in v1.1).
- Audit-log verification tool (`snoop-audit-verify`, §9.4).
- Agent-consumer missing primitives surfaced in W3-E2 that prove
  needed in production: scroll-without-select, progress-bar completion,
  `wpf_assert_absent` (if `presenceExpected: absent` in
  `wpf_wait_for_property` proves insufficient).

v2.0 (post-MVP, preserve v5-ideal sections):

- L3 `InputManager.ProcessInput` + `wpf_type_text` (pending S-2 detailed
  closure).
- L4 `SendInput` hardware input (audit-gated).
- Recording + replay with HMAC signing (MF-2 design required first).
- Observability subsystem (OTEL + ETW via TraceEvent).
- Capture subsystem (WGC, ΔE/SSIM diff).
- Virtual clock (`ITimeProvider` tier only; IL-rewrite deferred further).

v3.0+ considerations (were in v5-ideal):

- Multi-agent coordination.
- Session federation.
- Live edit mode (with XAML allowlist from MF-1).
- Time-travel replay.
- State-machine induction.

---

## 17. Tool count reconciliation

v5-ideal had 72 tools. v5-MVP ships 18 agent-visible + 4 utility = 22
total. Reduction: 69%. All cuts land in deferred milestones (v1.1 or
v2.0+), nothing abandoned.

Primary surface 18 tools = inside SOTA empirical sweet spot (10–20),
under Cursor's 40-tool cap, below Claude Desktop's context-bloat
threshold. Agent tool-selection accuracy maximized.

Wave-3 R-C argued for a further reduction to 10 tools (drop all L1
act tools, ancestors/children pagination, screenshot, `find_by_viewmodel`,
`trace_command`). That scope is documented as the alternative
aggressive-cut path — see §15. This revision pursues the 18-tool
honest-schedule path.

---

## 18. Non-goals recap (for clarity)

- MVP is not "release 1.0 of the product." It is the shippable subset
  that validates the thesis. Release 1.0 may be MVP + 2-3 weeks of
  polish + documentation.
- MVP is not "v5 with stuff removed." It is a scope-frozen document with
  different architecture contracts (session policy, locator type, state
  delta schema) that v5-ideal will inherit when v2.0 lands.
- MVP does not deprecate v5-ideal. v5-ideal is the destination; v5-MVP is
  the ship.

---

*End of PRD v5-MVP. 18 agent-visible tools + 4 utility. 3 milestones.
P50 18–22 weeks, P90 26–32 weeks. Wave-2 + Wave-3 findings applied. Ship.*
