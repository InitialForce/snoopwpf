# PRD v5 — Wave 2 Review Synthesis

Five parallel Opus reviewers, each briefed with PRD v5 + SOTA research synthesis
+ their specific lens. Cross-reviewer convergence is unusually high.

Full individual reports are archived in the conversation; this document is the
consolidated findings.

---

## Convergent critical findings (flagged by 3+ reviewers)

### C1 — 72 tools is a ship-blocker

- Reviewer A: 1.8× Cursor's hard cap, 5× the sweet spot.
- Reviewer D: "Cargo-cult architecture — designing for the system you wished
  clients had."
- SOTA §1.1: Pamela Fox (2026) documented total collapse at 107 tools.
- **Resolution**: Cut primary surface to 16–20 tools using Stagehand's
  act/observe/extract taxonomy. Reviewer A proposed explicit 16-tool surface:
  4 observe + 8 act + 4 extract, plus 4 meta-tools that group 35 of the
  remaining v5 tools.

### C2 — Post-action state delta missing

- Reviewer A: Currently `wpf_click` returns `{chosenFidelity, treeVersionBefore/
  After, elapsedMs, cacheHit}`. Missing the VeriGUI-critical fields.
- SOTA §1.4: 72.3% of GUI-agent failures are repeat-on-unchanged-screen loops.
- **Resolution**: Canonical shape for all mutation tools:
  `{success, chosenTier, elementVisible, stateChanged, currentFocus,
    treeVersionBefore, treeVersionAfter, treeVersionDelta, elapsedMs,
    cacheHit, actionabilityChecksPassed, failureReason, suggestion}`.
  Critical fields: `stateChanged` + `treeVersionDelta == 0` signals
  "don't retry; escalate."

### C3 — Polling-primary inversion required

- Reviewer A + D: Current §8.6 implies SSE-primary, polling-fallback.
- SOTA §1.2: No production MCP client (Claude Code, Desktop, Cursor, Cline)
  implements notifications. Closed as "not planned."
- Reviewer D: "Commits to a non-existent transport feature."
- **Resolution**: Rewrite §8.6 so `wpf_poll_changes` is the baseline
  contract, `wpf_wait_for_changes` (long-poll) is recommended client default,
  SSE is labeled "experimental — 2027+ when client support exists." Revise
  §1.2 thesis to remove "zero polling" language.

### C4 — Spikes must close before M1 code lands

- Reviewer B + D: Three critical-path spikes have binary outcomes:
  - **S-2 UnsafeAccessor**: if signatures differ on net6/8/9 WPF, L3/L4 input
    is permanently off the table (unless replaced with HarmonyX).
  - **S-3 tree-change detection without VisualDiagnostics**: multi-source
    feed design is unvalidated on real apps.
  - **S-4 virtual clock feasibility**: `ITimeProvider` adoption audit in
    MotionCatalyst will reveal hundreds of raw `DispatcherTimer` calls;
    Tier B/C cost becomes quantifiable.
- **Resolution**: No M1+ beads until S-1 through S-5 close with results
  written into the PRD.

### C5 — Critical code bugs in current snoopwpf already

- `/c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopAgent.cs:80` — `Console.WriteLine`
  on stdio path, corrupts MCP framing. Single-line fix.
- `/c/work/snoopwpf/SnoopWPF.Agent.Server/McpServerSetup.cs:84` — same.
- `/c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/NodeRegistry.cs:75-84`
  — race condition in concurrent registration; two callers can end up with
  different IDs for the same object.
- `/c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/CursorManager.cs:113-117`
  — consumed-cursor indistinguishable from not-found.
- `/c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/RedactionFilter.cs:61-65`
  — `PasswordBox.Password` check condition will never match.
- `/c/work/snoopwpf/SnoopWPF.Agent.Engine/SnoopInspector.cs:1370-1373`
  — error-code ordering bug: cancellation racing with DispatcherBusy.
- **Resolution**: Fix inline, before input tools are added.

### C6 — PRD is whitepaper, not shippable engineering doc

- Reviewer D: 1,837 lines of spec vs 4,000 LOC existing code. 176 user stories
  = 2.5–4 years at 1-engineer capacity. User asked for 4 months of work.
- **Resolution**: Write `PRD-v5-MVP.md` (~400 lines) covering only the
  "5 winning things." Preserve v5 as the architectural North Star for
  releases 2.0+. Do NOT rewrite v5 as a smaller v6 — it will rebalance
  back to 1,500 lines in one review cycle.

### C7 — Stale-handle risk: `nodeId: "0:N"` needs lazy locator

- Reviewer A + B: Playwright deprecated `ElementHandle` for exactly this.
  Our `nodeId` strings are opaque handles that go stale under tree mutation,
  virtualization, navigation.
- **Resolution**: Introduce `WpfLocator` type before input tools land:
  `{ "$locator": "automationId=StartButton" }`. All tools accept both
  `nodeId` and locator. `wpf_find_elements` returns both. Deprecate
  `nodeId` as primary reference via analyzer `SWPF0010`.

### C8 — Idle-resource AND-gate contract missing

- Reviewer A + B: v5 §8.2 has tree versioning but no `IIdlingResource`
  contract. `wpf_pump_until_idle` is underspecified.
- SOTA §2.3, §2.4: Detox/Espresso aggregate-idle is the dominant SOTA pattern.
- **Resolution**: Add §8.2.5 "Idle-resource AND-gate": `IIdlingResource`
  interface with `IsIdle` + `IdleChanged`. Built-ins: Dispatcher, Composition
  Rendering, Storyboard, DispatcherTimer, CustomApp. `wpf_pump_until_idle`
  throws `AnimationRunawayException` after configurable ceiling (Flutter
  `pumpAndSettle` parity).

---

## Security findings (Wave 2 security re-review)

All 12 v4 findings verified addressed in v5 (2 PARTIAL, 10 FIXED).

**9 new findings from v5 additions:**

| ID | Severity | Finding | Fix before |
|----|----------|---------|-----------|
| MF-1 | Critical | XAML sandbox allowlist undefined; `ObjectDataProvider` is a known XAML RCE gadget | M10 |
| MF-2 | Critical | HMAC key derivation unspecified in §9.1; key-as-session-token unsafe | M7 |
| MF-3 | High | Federation token entropy / mutual auth unspecified | M9 |
| MF-4 | High | Binary-search exfiltration via cross-property correlation in `find_by_viewmodel` | M5 |
| MF-5 | High | Predicate DSL amortized CPU budget + size cap missing | M3 |
| MF-6 | Medium | Arbiter role self-claim bypass | M9 |
| MF-7 | Medium | Screenshots are not default-deny; baseline PNGs capture rendered PII | M6 |
| MF-8 | Medium | `DOTNET_STARTUP_HOOKS` assembly verification | M1 |
| MF-9 | Low | Insider recording exfiltration — explicit field filter needed | M7 |

Full exploit flows in conversation archive.

---

## Codebase findings

### snoopwpf (branch `develop`, currently at BEAD-026)

**Surprise: implementation is further along than BEADS.md suggests.**
- `GetAncestorsAsync`, `FindElementsAsync`, `SetPropertyAsync` are NOT
  `NotImplementedException` — they are fully implemented.
- 8 projects already exist (Contracts, Engine, Server, Tools, Remote,
  Injection, Host, Cli), not 4.
- Full integration test suite present; `FakeSnoopInspector` pattern already
  established.

**Before-M2 refactors** (Reviewer B):
- Add `IInputStrategySelector`, `IDeterministicInputStrategy`,
  `IProbabilisticInputStrategy` to `Contracts`. Do not add input methods
  to `ISnoopInspector` (keep it reads-only).
- Create `SnoopWPF.Agent.Input.Deterministic` + `SnoopWPF.Agent.Input
  .Probabilistic` projects. Don't preemptively split Engine; it's 1,200 LOC
  and healthy.
- Add `MaxFidelity`, `EnableAutomation`, `MultiAgentRole`, `RecordingPolicy`
  fields to `SnoopAgentOptions` before input tools arrive.

**Project structure recommendation**: Don't add Capture, Recording,
Observability, MultiAgent, LiveEdit projects now. Add them when their
milestones start. Premature creation adds DI ceremony without value.

### wpf-mcp (branch `wpf-mcp`)

**Migration readiness: GREEN overall, YELLOW in 4 specific areas, RED on
CI dual-stack gate.**

Actual Console.Write* offenders discovered:
- `AnalysisView.xaml.cs:74`
- `BertecSdk.cs:60`
- (CliFx is safe — uses `IConsole` abstraction.)

Boot sequence constraint: MotionCatalyst spawns a GUI thread named
"GUI Thread" in `AsyncApp.Start()`. `SnoopAgent.StartCoLocated` must fire
via `_app.Dispatcher.BeginInvoke` from that thread, not `Program.Main`.
`Console.Out` takeover must happen at `Program.Main` line 1 (before CliFx).

**MC-specific PRD v5 gaps** (not in v5 but MC needs):
1. `wpf_set_slider_value` with range normalization.
2. Virtualized-list scroll + partial-text match (v5 `wpf_select_item`
   assumes exact match).
3. Branding-baseline management (CI artifact store integration).
4. Multi-step state-machine reset (`AppSession.ResetToHome` 150-line
   fallback logic).
5. License-dialog detection during launch.
6. Hardware-device-readiness gate (may not be reflected in WPF tree).
7. Multi-product launch param (`-p SwingCatalyst` etc.).
8. `--qa-mode` software rendering must not conflict with v5 DirectX/WGC
   capture paths.

**Estimated M2 scenario coverage**: 70%. Gaps are slider, hardware-wait,
visual-diff baseline management.

**MCP wrapper deletion**: ~40% at M2 (1,900 LOC), ~80% by Phase 4.

---

## Tech stack updates required

From SOTA research (verified by Reviewer A):

| v5 says | SOTA says | Change |
|---------|-----------|--------|
| `Reflection.Emit` / `AsmResolver` for virtual clock Tier C | HarmonyX / MonoDetour | Rewrite §8.7 to name these |
| LoadLibrary + CreateRemoteThread implied | `DOTNET_STARTUP_HOOKS` for .NET 8+ | Add §5.3.4 "Injection strategy by runtime" |
| `RenderTargetBitmap`-only capture | Windows.Graphics.Capture for D3D/window | Add WGC path to §7.1 |
| Generic ETW mentions | `TraceEvent` + `EventPipe` named | Update §10.2 with NuGet names |

---

## The 5 winning things

Reviewer D proposed, others corroborate:

1. **Co-located in-process inspection** — the 15 v3 tools, hardened.
   No competitor has this with pagination + redaction + security model.
   plop44/SnoopWpfMcp has ~3 tools with none of the polish.

2. **L0/L1 input with honest naming** — 8 tools:
   `wpf_execute_command`, `wpf_set_text_value`, `wpf_set_check_state`,
   `wpf_select_item` (L0); `wpf_click`, `wpf_toggle`, `wpf_expand_collapse`,
   `wpf_type_text` (L1/L3). Covers 90% of MotionCatalyst scenarios.
   No L3/L4 spike dependency.

3. **Normalized post-action state delta** on every mutation tool.
   VeriGUI fix. Eliminates the 72.3% repeat-action-on-unchanged-state
   agent failure class.

4. **`wpf_wait_for_property` + `wpf_poll_changes`**. Two sync tools,
   polling-primary. Replace every `Task.Delay` in consumer code. Idle-
   resource AND-gate contract backing them.

5. **Semantic navigation** — `wpf_find_by_viewmodel`, `wpf_trace_command`,
   `wpf_resolve_binding`. Three tools that no cross-process UIA system
   can ever provide. The architectural moat.

Total: **20 tools** if we ship the original 15 + 5 new input tools with
the semantic 3. Exactly at the SOTA sweet spot.

Estimated delivery for 1-engineer team: **11–17 weeks** (roughly 4 months).
The original v5 plan taken literally: 2.5–4 years.

---

## Proposed success metrics

1. Claude Code, given only `wpf_get_session_info` as a hint, navigates to
   any named element in MotionCatalyst's main window within 3 tool calls,
   p95.
2. All 25 MotionCatalyst SpecFlow scenarios pass headlessly with p95
   per-tool-call < 10 ms and zero `Task.Delay` calls in any script.
3. Agent repeat-on-unchanged-state failure rate drops below 5% over a
   100-scenario run (VeriGUI industry baseline: 72.3%).
4. `wpf_find_by_viewmodel` correctly locates the bound ViewModel for
   every interactive element in MotionCatalyst's session-list screen
   within one tool call, p99.
5. Zero confirmed secret-in-property-dump incidents in a 1,000-property
   read corpus drawn from MotionCatalyst live state.

---

## Recommended next step

**Write `PRD-v5-MVP.md`** — a disciplined ~400-line scope-frozen document
covering the five winning things plus:
- 20 user stories (not 176)
- 3 milestones (M0 spikes, M1 foundation, M2 ship)
- Hard tool-count cap of 20
- Polling-primary sync model
- All critical security findings (MF-1 through MF-9) applied inline
- Normalized post-action state delta as a canonical schema
- `WpfLocator` type spec
- No state-machine induction, no federation, no multi-agent driver lock

v5 remains on disk as the long-term architecture spec — nothing wasted.
v5-MVP is what teams ship from.
