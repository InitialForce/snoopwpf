# SOTA Research Findings — Wave 1 Synthesis

Source: 10 parallel Sonnet web-research agents, April 2026. Each cited current
(2024-2026) sources. This document is the input to Wave 2 PRD review.

---

## 1. Hard ceilings and convergent patterns

### 1.1 Tool count cliff (MCP ecosystem)

- **Cursor enforces a hard 40-tool cap**; tools 41+ silently dropped.
- **GitHub Copilot benchmarked** 13 core tools vs 40+ → 94.5% task coverage vs 69%.
- **Pamela Fox (2026)** strict-schema study: perfect at 10 tools, 19/20 at 20 tools,
  **total collapse at 107 tools**.
- **40 Playwright MCP tools** consume ~22% of Claude Sonnet 200k context.
- **Our PRD v5: 72 tools** — 1.8× the hard ceiling, 5× the sweet spot.

### 1.2 MCP notifications are a fantasy

- **Claude Code**: issue #4157 closed **"not planned"**.
- **Claude Desktop, Cursor, Cline**: no visible notification UI.
- All `notifications/progress`, `notifications/*` paths in MCP spec are
  effectively unimplemented in every production client.
- **PRD v5 §8.6** SSE subscriptions must be treated as a 2027+ feature. Design
  for polling primary; notifications optional.

### 1.3 Structural > pixel action space

- **OSWorld ablation (NeurIPS 2024)**: screenshot-only = 5.26% task success.
  Screenshot + a11y tree combined = significantly higher.
- **Top OSWorld agent (Agent S2, 2025)**: 34.5% success, uses
  Mixture-of-Grounding with structural expert.
- **Browser-Use**: DOM/CSS-first, pixel-fallback — outperforms screenshot-only.
- **Playwright**: locator-first, coordinate-fallback.
- **GUI-Actor (Microsoft, 2025)**: eliminates pixel coords entirely in favour of
  visual-patch attention tokens; 7B model beats UI-TARS-72B.
- **Implication**: WPF has `AutomationId`/`Name`/`ControlType` natively. Our
  PRD is structural-first already — but we must double down and ensure pixel
  coordinates are never primary.

### 1.4 Post-action state delta is mandatory (VeriGUI 2026)

- **72.3% of all GUI-agent failures** are "execution timeouts caused by agents
  repeating the same failed action on an unchanged screen" (arxiv:2604.05477).
- TVAE cycle (Think → Verify → Act → Expect) lifts success 0% → 16.7% on
  hardest benchmark.
- BacktrackAgent: +7.59% TSR on Mobile3M with verifier modules.
- **Every tool call must return a structured state delta**:
  `{ success, element_visible, state_changed, current_focus, tree_version_delta }`.
- Our PRD v5 has `treeVersionBefore/After` but not the normalized delta
  structure that research shows works.

### 1.5 Tool description quality controls success

- **arxiv:2602.14878**: 97.1% of real MCP tools have quality defects.
- **+5.85pp task success** from descriptions with
  Purpose + Guidelines + Limitations + Parameter format.
- **+15.12% evaluator performance** from the same.
- Our PRD v5 mentions this in §15.5 but doesn't enforce it in a schema.

---

## 2. Proven architectural patterns (steal these)

### 2.1 Playwright "lazy locator"

- **Locator stores the query string, not an element reference.**
- Re-resolves the DOM on every action. Retry is automatic; zero-config.
- Playwright deprecated `ElementHandle` because handles go stale.
- **Our PRD uses `nodeId: "0:N"`** — this IS a handle. Stale-handle failures
  under tree mutation are a design risk.
- **Fix**: add a `locator` type that encodes the query, re-resolves on each call.

### 2.2 Playwright "actionability checklist"

- Per-action-type, not global: `click` requires `attached + visible + stable
  bounding box + receives events + enabled`. `fill` skips stability.
- Timeout fires `TimeoutError` with which-check-failed detail.
- **Our PRD v5** has `wpf_wait_for_property`, but no per-action actionability
  gate built into `wpf_click` / `wpf_type_text`.

### 2.3 Detox "idle resource AND-gate"

- All synchronization via `DTXSyncManager`: register N idle resources;
  proceed only when **all** are simultaneously idle.
- Espresso's `IdlingResource` is the same pattern.
- **Never polls on wall-clock**; uses callback-based "all-idle" signal.
- **Our PRD v5 §8.2** has tree versioning but not the AND-gate contract.
- Resources for WPF: `DispatcherIdle`, `CompositionTarget.Rendering`,
  `Storyboard.Completed`, active `DispatcherTimer`, HTTP-pending (via
  `DiagnosticListener`), app-declared custom.

### 2.4 Flutter `tester.pump()` / `pumpAndSettle()`

- Frame-pump primitive: advance one frame, process microtasks.
- `pumpAndSettle()`: repeatedly pump until no frames scheduled; throws on
  infinite animation.
- Every modern framework has this: Compose (implicit), Avalonia `RunJobs()`,
  XCUITest predicate expectations.
- **Our PRD v5** has `wpf_pump_dispatcher` — need to strengthen to
  `wpf_pump_until_idle_with_animation_aware` parity.

### 2.5 Stagehand's act/observe/extract separation

- Three primitive categories, three distinct tool families.
- `observe` returns structured state (doesn't mutate).
- `act` performs one side effect.
- `extract` pulls structured data from the view.
- **Our PRD v5** mixes these. `wpf_inspect_element` is observe;
  `wpf_click` is act; `wpf_resolve_binding` is extract. Stagehand names them
  explicitly as categories — useful for agent reasoning.

### 2.6 Cypress differential DOM snapshots

- Snapshots are **diffs**, <300 KB per step, not full trees.
- Streamed to cloud for time-travel scrubbing.
- **Our PRD v5 §9** has recordings but doesn't specify diff-only snapshots.
  Storage explodes if we capture full trees per step.

### 2.7 Angular CDK component harnesses

- Control vendors ship their own test harness per control.
- Same harness works in-process (bUnit-style) and E2E (Selenium).
- **Our PRD v5 is missing this** — MotionCatalyst's custom controls
  (video viewer, skeleton render, etc.) have no harness contract.

### 2.8 Sentry default-deny text masking

- All text obfuscated by default; opt-in allowlist for safe fields.
- Opposite of LogRocket's opt-out.
- **Our PRD v5 §11.2** has structural redaction but on-recording basis;
  Sentry's default-deny is stronger for screenshot/visual-diff artifacts.

### 2.9 rrweb delta recording with coalescing

- Full snapshot on init + `MutationObserver` incremental deltas.
- Coalesce mutations on same node within a frame to final value.
- Snappy compression → 100–500 KB per 5-minute session.
- **Our PRD v5 §9** needs this detail — byte-addressable block storage,
  content-addressed, JSONL blocks.

### 2.10 Chrome Trace / Perfetto as trace format

- De-facto standard for UI automation timing.
- Chrome JSON event format or Perfetto protobuf.
- Perfetto handles 10× more events than Catapult via WASM + SQL queries.
- **Our PRD v5 §10** mentions Chrome trace export but doesn't make it
  canonical.

---

## 3. Technical upgrades from SOTA

### 3.1 DOTNET_STARTUP_HOOKS replaces LoadLibrary injection

- **LoadLibrary + CreateRemoteThread is .NET Framework-era.**
- On .NET 8, the CLR is already loaded in-process for WPF targets; injecting
  a second runtime instance causes conflicts.
- `DOTNET_STARTUP_HOOKS` env var + `StartupHook.Initialize()` runs before
  `Main()` — officially supported, zero native bootstrap.
- `InjectDotnet` NuGet (0.4.0, 2024) for external injection into running
  processes, managed-first.
- **Our PRD/v3 injection path** uses `Snoop.InjectorLauncher` → native DLL —
  this is the old path. Keep for .NET Framework targets; use
  `DOTNET_STARTUP_HOOKS` for .NET 8+ targets.

### 3.2 HarmonyX / MonoMod.RuntimeDetour for IL rewrite

- Raw `Reflection.Emit` has no hook-ordering guarantees; breaks under
  Tiered JIT recompilation.
- **HarmonyX** (ILHook) + **MonoDetour** (2024) handle native trampoline
  invalidation correctly.
- Validated against .NET 8. `Hook` sufficient for `DateTime.UtcNow`;
  `ILHook` needed for call-site rewrites.
- **Our PRD v5 §8.7 tier C** calls for "Reflection.Emit or AsmResolver".
  Replace with HarmonyX / MonoDetour.

### 3.3 Windows.Graphics.Capture replaces DXGI Desktop Duplication

- WGC is per-window, GPU-zero-copy, BGRA8 top-down format native.
- Windows 11 24H2 fixed frame-pacing bugs; DXGI unreliable on multi-GPU.
- Accessible via `IGraphicsCaptureItemInterop` COM interop + `GraphicsCaptureItem
  .CreateFromWindowId()`.
- For D3DImage/D3DSurface: shared-texture via `D3D11_RESOURCE_MISC_SHARED` +
  `OpenSharedResource` is GPU-zero-copy.
- **Our PRD v5 §7** specifies `RenderTargetBitmap` only. Add WGC as the
  window-capture path.

### 3.4 TraceEvent + EventPipe for ETW

- `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet (actively maintained) for
  subscribing to WPF ETW provider (`E13B77A8-14B6-11DE-8069-001B212B5009`).
- `EventPipe` via `Microsoft.Diagnostics.NETCore.Client` streams structured
  events programmatically.
- DiagnosticListener is **not integrated** with WPF; don't try to bridge.
- **Our PRD v5 §10.2** mentions ETW but doesn't name TraceEvent / EventPipe.

### 3.5 AgentRx 9-category failure taxonomy

- Microsoft Research (2025): structured failure taxonomy for AI-agent debugging
  beats raw traces for LLM self-correction.
- Categories: Plan Adherence, Invalid Invocation, Parameter Drift, etc.
- **Our PRD v5 §10** error taxonomy is flat; AgentRx gives us a richer
  diagnostic layer.

---

## 4. Competitive intel

### 4.1 Direct competitor: `plop44/SnoopWpfMcp` exists

- GitHub repo, 4 stars, 4 commits, early 2025.
- **Exactly our architecture**: SnoopWPF injection + Named Pipes + MCP.
- Tools: `get_visual_tree`, `invoke_automation_peer`, `take_wpf_screenshot`.
- Very early-stage, thin on features — our scope dwarfs this, but we must
  acknowledge and differentiate.

### 4.2 WinAppDriver abandoned

- No releases since 2021. Microsoft: "no timeframe for resumed investment."
- **`appium-windows-driver`** is the community successor.
- Windows-side UIA cross-process automation has no active Microsoft maintainer.

### 4.3 Microsoft's first-party answer

- **Copilot Studio Computer Use** (Ignite 2025): UIA + vision hybrid,
  isolated Windows account. High-level intent, not programmatic.
- **UFO2 Desktop AgentOS** (Microsoft Research paper, 2025). Competitive in
  concept but not in our technical niche (in-process WPF).
- **Windows 11 MCP Registry** (Build 2025): OS-level MCP for File
  Explorer / Settings. No app-level exposure — apps opt in.
- **GUI-Actor** (Microsoft Research 2025): coord-free visual grounding.
  Research-stage.
- **CoreAutomationRemoteOperation**: batches UIA cross-process calls. Still
  cross-process — doesn't solve our problem.

### 4.4 Microsoft's gaps we fill

- No first-party in-process WPF automation surface.
- No ViewModel / DataContext / Binding introspection.
- No live property mutation via the WPF property system.
- No `ICommand` reverse lookup or invocation.
- No MCP surface for WPF apps.

---

## 5. Specific PRD v5 gaps surfaced by research

### 5.1 Missing primitives

| Missing in v5 | Source | Priority |
|---------------|--------|----------|
| Per-action actionability checklist (Playwright) | Wave-1-A | High |
| Lazy-locator type (not nodeId handle) (Playwright) | Wave-1-A | High |
| Idle-resource AND-gate (Detox/Espresso) | Wave-1-D | High |
| `pump_and_settle` with animation awareness (Flutter) | Wave-1-D, Wave-1-E | High |
| Act/observe/extract tool categorization (Stagehand) | Wave-1-F | Medium |
| Component harness contract (Angular CDK) | Wave-1-E | Medium |
| Diff-only snapshots in recordings (Cypress/rrweb) | Wave-1-B, Wave-1-I | High |
| Normalized post-action state delta (VeriGUI) | Wave-1-H | **Critical** |
| AgentRx failure taxonomy | Wave-1-I | Medium |
| Sentry default-deny text masking | Wave-1-I | High (security) |

### 5.2 Design choices to reconsider

| v5 choice | Research says | Recommendation |
|-----------|---------------|----------------|
| 72 tools | Hard ceiling 40; sweet spot ~15 | Cut to ~20 primary, rest as sub-categories |
| SSE subscriptions primary | No client supports notifications | Polling primary; SSE experimental |
| Custom MobileCLIP diff | SSIM covers 95% of cases | Keep ΔE + SSIM; skip CLIP until needed |
| Virtual clock 3-tier | Tier C (IL rewrite) via `Reflection.Emit` | Use HarmonyX/MonoDetour |
| DXGI Desktop Duplication implied | Deprecated Windows 11 24H2+ | Use Windows.Graphics.Capture |
| Raw LoadLibrary injection for .NET 8 | CLR-conflict risk | Use DOTNET_STARTUP_HOOKS |
| 11 milestones | Scope likely still too large | Merge + cut ruthlessly |

### 5.3 Additions nobody in the ecosystem has yet (still beyond-SOTA)

- Co-located MCP hosting in the target process (no one does this).
- Multi-dispatcher first-class (mobile frameworks often mono-queue).
- State-machine induction from recordings (research-only elsewhere).
- Semantic navigation tools (`find_by_viewmodel`) — ecosystem is visual-first.
- Time-travel + causal lineage together (only replay.io comes close).
- Live edit mode (XAML hot-replace during automation) — unique.

---

## 6. Summary verdict for PRD v5

**Sound directionally but must cut and retune.**

- **Scope**: Cut tool surface by 60-70% for first ship; expose extended
  capabilities via meta-tools.
- **Sync story**: Adopt Detox's AND-gate formally; idle resources are the
  primitive, not tree-versioning alone.
- **Locators**: Add lazy re-resolving locator type; deprecate raw `nodeId` as
  a first-class reference.
- **State delta**: Normalize post-action response shape across all mutation
  tools (VeriGUI finding).
- **Notifications**: Design for polling; treat SSE as future-compatible.
- **Tech stack updates**: DOTNET_STARTUP_HOOKS, HarmonyX, WGC, TraceEvent.
- **Differentiation**: Acknowledge plop44/SnoopWpfMcp but our scope is 50×.

Wave 2 Opus reviews build on this baseline.
