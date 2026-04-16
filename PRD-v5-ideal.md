# PRD v5: SnoopWPF.Agent — The Ideal Architecture

> **This document is the North Star.** Every finding from the v4 multi-reviewer
> pass (GPT-5.4-Pro strategic review + 4 Opus specialist agents: architecture,
> WPF internals, security, MCP UX) is applied. Every feature v4 deferred is
> included. Several capabilities beyond current SOTA are added. The ambition is
> "what would an advanced civilization design if they had infinite engineering
> budget and all design constraints had to be honoured in one coherent system?"
>
> This is the full-scope design target. A separate MVP-slicing document can
> always be extracted from it. v4 remains on disk as the pragmatic-realist
> reference; v5 is not a redline on v4 — it is the rewrite that treats v4 as
> insufficient.

---

## 0. Reading Guide and Change Log

### 0.1 Reading order

- §1–§3 establish philosophy, scope, and non-goals.
- §4 defines quality gates and the extended milestone sequence (11 milestones).
- §5 is architecture core. **Read before any subsystem section.**
- §6–§12 are subsystem designs. Orthogonal; read in any order.
- §13–§15 cover security, performance, tool inventory.
- §16–§18 define user stories, milestones, risks.
- §19 collects open questions that still require human decisions.
- Appendices hold internal-API reference, threat model, perf methodology, and
  multi-agent protocols.

### 0.2 Change log vs PRD v4

Every v4 design decision that a reviewer flagged is addressed below. Nothing
is deferred; features v4 called "deferred to future" are first-class in v5.

| Area | v4 stance | v5 resolution |
|---|---|---|
| Tiered input as single `IInputStrategy` | Single interface | Split into `IDeterministicInputStrategy` + `IProbabilisticInputStrategy` with discriminated-union selector return type (§6.2) |
| `click` / `type_text` auto-select picks L0 | Default for bound cases | **Renamed and split.** `wpf_click` defaults to L1/L3 (faithful). L0 exposed as distinct `wpf_execute_command` / `wpf_set_text_value` tools (§6.5). No semantic lying. |
| 3×5 mode/fidelity matrix | Per-call fidelity parameter | **Mode is session-scoped policy.** Capability ceiling stamped at session creation. Per-call fidelity becomes a hint within the session's allowed range. (§5.4) |
| Virtual clock via `DispatcherTimer._timeManager` | "Best-effort hook" | **Hook path retracted** (field doesn't exist). Virtual clock uses `ITimeProvider` injection + `DispatcherTimerFactory` cooperative replacement + IL-rewrite tier for the last 10% (§8.7). |
| Tree versioning via `VisualDiagnostics.VisualTreeChanged` | Assumed | **Replaced with multi-source tracker** — per-node `Loaded`/`Unloaded` + `CollectionChanged` on `InternalChildren` + `CompositionTarget.Rendering` frame walks + explicit `Bump()`. Works without a debugger attached (§8.2). |
| `DependencyPropertyDescriptor.AddValueChanged` | Weak refs on node | **Switched to `ValueChangedEventManager`** weak-event pattern. No leaks. (§8.3) |
| `UnsafeAccessor` signatures | Two-overload `InputReportEventArgs` | **Single-overload accessor** taking `InputReport` base class. Verified at JIT time. (Appendix A) |
| L3 dispatcher priority | `Input` | **`Send`/`Normal`** — matches real `HwndSource.FilterMessage` behaviour. (§6.3) |
| Headless `HwndSource` | Assumed present | **Required precondition**: `SnoopAgent.StartCoLocated` creates a hidden 1×1 message-only `HwndSource` if none exists (§5.3.2). |
| Stdout collision | Documented risk | **Hard takeover**: `Console.Out` replaced with a discard sink on agent start, before any other init. Third-party libraries constrained via module-load prohibition list. (§5.3.3) |
| Recording redaction | Keyword list | **Structural redaction** — `wpf_type_text`'s `text` param is treated sensitive-by-default. Per-field opt-out. (§11.2) |
| `wpf_replay` path traversal | Unconstrained | **HMAC-signed recordings**; path constrained to baseline root; PID + start-time identity. (§11.4) |
| `wpf_set_property` fidelity bypass | Gate at tool layer | **Gate at strategy layer** — any invocation through any entry point passes `EnableAutomation` check. (§11.1) |
| Audit logging | Free-form stderr | **Structured out-of-process audit log** (JSONL with monotonic sequence, HMAC'd). Stderr carries summary only. (§11.5) |
| Predicate DSL | No versioning | **`$version: 1` field required**; typed schema; 64 KB resolved-value cap; evaluation time quota. (§8.5) |
| Subscription delivery | SSE over MCP notifications | **Three-mode delivery** — SSE for capable clients, long-poll variant (`wpf_wait_for_changes`), and request-response `wpf_poll_changes(sinceVersion)` for everyone else. (§8.6) |
| Recording's hidden coupling | Imports Input + Sync internals | **`IInputEventSource` + `ITreeVersionSource` in Contracts**; Recording depends on contracts only. (§5.6) |
| `wpf_find_by_viewmodel` exfiltration | Raw reflection | **Redaction check applied to `propertyPath`**; short-name matching default; evaluation quotas. (§9.1) |
| Tool surface cognitive load | 7-value `fidelity` param on every tool | **`fidelity` hidden from primary schema**; accessible via `advanced.fidelity` nested object. Default auto-select with profile-guided cache. (§6.2, §15.3) |
| Delivery-mode story | 1 mode primary, 2 secondary | **4 integration modes** — co-located, NuGet-in-process, Injection, and Shared-Memory (for zero-copy high-throughput scenarios). (§5.2) |
| Multi-dispatcher | "Optional, single covers 95%" | **First-class from day one.** Node IDs encode dispatcher; automation surface is dispatcher-aware; cross-dispatcher input is linearized. (§5.8) |
| Migration adapter | One-line note | **First-class milestone M-MIGRATE** with `McpDriver` shim, dual-stack runtime, CI gate for behavioural equivalence. (§16) |

### 0.3 Additions beyond v4 (new capabilities)

- **Observability subsystem** (§10) — OpenTelemetry spans per tool call, ETW event correlation, Chrome trace export, accessibility compliance scoring.
- **Multi-agent coordination** (§11, Appendix C) — session fan-out, read-only observers, driver arbitration.
- **Live edit mode** (§12) — runtime XAML injection, style/template hot replace, scoped.
- **Time-travel replay** (§9.4) — step backwards through recorded tree states, causal lineage graph.
- **Continuous passive recording** (§9.5) — ring-buffer mode always on; snapshot-on-anomaly.
- **Profile-guided fidelity cache** (§6.4) — observed success rates per element signature feed the auto-selector.
- **Temporal predicates** (§8.5.3) — `"X occurred within 500 ms of Y"`, `"Z stable for 200 ms"`.
- **Perceptual visual diff** via MobileCLIP embeddings (§7.3) — catches semantic regressions that pixel diff misses.
- **Out-of-band binary channel** (§5.7) — screenshots and blobs travel via shared-memory or HTTP blob-ref, not inline JSON base64.
- **Formal state-machine induction** (§9.6, appendix D) — from recorded sessions, induce a Mealy machine of the app's UI. Property-based test generation on top.
- **Session federation** (Appendix C) — one app's MCP surface becomes a reachable peer of another app's, forming a graph of cooperating automation targets.

---

## 1. Overview and Problem Statement

### 1.1 What v5 is

SnoopWPF.Agent v5 is an in-process, co-located-first, MCP-native automation and
inspection surface for WPF applications. It replaces cross-process UI-Automation
tooling (UIA / FlaUI / WinAppDriver) for owned applications, offering:

- **Sub-millisecond reads** via cached visual-tree snapshots, never cross-process.
- **Five tiers of input fidelity** with honest semantic naming — no disguised
  shortcuts.
- **Event-driven synchronization** — zero polling in the success path.
- **Semantic navigation** — the automation surface speaks ViewModel, Command,
  Binding, not just `FrameworkElement`.
- **Multi-dispatcher first-class** — applications with multiple Dispatchers
  work identically to single-threaded apps.
- **Multi-agent capable** — more than one AI client can attach concurrently,
  with driver-arbitration.
- **Deterministic recording and replay** — HMAC-signed, time-travel capable,
  with causal lineage.
- **Headless parity** — every tool works identically with no visible window.
- **Injection mode available** for third-party apps with reduced-capability
  ceiling documented at the session level.

### 1.2 Refined thesis

Replaces v4's "strictly better." Revised thesis (GPT-Pro suggestion adopted):

> **For owned WPF applications, an in-process co-located MCP server with
> honestly-named fidelity tiers and event-driven synchronization is the best
> architecture for the class of automation tasks that blend semantic reasoning
> and UI fidelity.**
>
> Shortcut invocation (L0 command execution) and faithful invocation (L3 input
> manager) are **distinct products** — surfaced as distinct tools — not
> interchangeable fidelities behind one name.
>
> Cross-process UI-Automation retains a role as an **accessibility conformance
> smoke check**, not as the primary automation driver.

### 1.3 Target users

- Claude Code and Claude Desktop driving QA of owned WPF apps.
- Developers writing automated scenarios via `snoop-cli` or MCP tools.
- Reference consumer: **MotionCatalyst** (companion integration plan at
  `/c/work/desktop/wpf-mcp/docs/plans/snoop-integration-plan.md`).
- Third-party closed-source WPF apps via injection mode (reduced ceiling).
- Multi-agent research: several coordinating AI agents driving one app.

---

## 2. Goals

### 2.1 Primary goals

- **Faithful invocation**: every user-visible action in an app is reachable
  through a tool whose behaviour is semantically indistinguishable from the
  corresponding user gesture.
- **Semantic invocation**: every user-visible action is also reachable through
  a tool that speaks the application's domain model, bypassing the visual
  pipeline where tests only care about business logic.
- **Zero polling success path**: any success path should use event-driven
  notification with bounded fallback polling only as a degradation.
- **Determinism on replay**: within the deterministic-clock option, a recorded
  input sequence replays byte-identically.
- **Headless identity**: every tool is behaviourally identical whether the
  target app has a visible window or not.
- **Multi-dispatcher identity**: dispatcher count is observable but not a
  tool-shape-changer.

### 2.2 Performance goals

- **Read tools**: p99 < 1 ms for cached-tree paths, < 5 ms for uncached.
- **Non-hardware input**: p99 < 5 ms; hardware input p99 < 20 ms (focus race).
- **Subscription event delivery**: p99 < 10 ms from Dispatcher fire to
  transport delivery.
- **Tree version mutation**: p99 bump cost < 1 µs on 1,000 watched nodes.
- **Agent startup overhead**: < 200 ms from process start to first tool
  answerable.

### 2.3 Quality goals

- **Open-source merge-ability** with upstream snoopwpf; additive structure.
- **Coverage**: every MCP tool has an integration test executing against the
  sample app; every fidelity tier has a conformance test.
- **Observability**: every tool call emits structured OpenTelemetry spans.
- **Multi-runtime**: .NET 6, .NET 8, .NET 9 (and .NET 10 when released) all
  in matrix CI.
- **Sandbox strictness**: predicate DSL, path handling, and recording I/O
  all sit inside explicit boundaries with fuzz tests.

---

## 3. Non-Goals

Deliberately kept narrow. Anything that could be in scope is in scope; the
non-goals are constraints we permanently accept.

- **Cross-platform**: WPF is Windows-only. No ambition here.
- **Non-WPF UI frameworks**: WinForms, MAUI, Avalonia, UWP, WinUI 3, XAML
  Islands, browser DOMs — all separate products, not v5.
- **Remote hostname**: automation binds to `127.0.0.1` and local pipes only.
  Remote debugging is a later security review.
- **Production automation surfaces in shipping consumer apps**: the `--mcp`
  flag must remain opt-in; shipping builds do not expose MCP by default.
- **Replacing the Snoop GUI**: Snoop.exe remains untouched; v5 runs alongside.
- **Implementing a test runner**: we ship MCP tools. Scenario runners
  (SpecFlow, xUnit harness, etc.) stay in consumer repos.
- **Backwards compatibility with v3 consumers who depended on deferred-to-v2
  tool absence**: `wpf_invoke_method` and peers ship in v5; consumers pinning
  a tool list must explicitly ignore new tools.
- **Drop-in FlaUI API compatibility**: we provide a migration adapter
  (§16.2), not binary compatibility.

---

## 4. Quality Gates

### 4.1 Per milestone

Each milestone inherits all prior gates. Eleven milestones instead of v4's six.

**M0 Spikes** (unblock before any other code lands):
- Spike S-1: Co-located hosting stdio round-trip against a bare WPF app.
- Spike S-2: `UnsafeAccessor` against `RawKeyboardInputReport` /
  `RawMouseInputReport` / `InputReportEventArgs` on net6, net8, net9.
  Including a headless `HwndSource` setup test.
- Spike S-3: Tree-change detection without `VisualDiagnostics.VisualTreeChanged`
  — benchmark per-node `Loaded`/`Unloaded` + frame-polling vs. internal
  `InternalChildren.CollectionChanged` reflection.
- Spike S-4: `ITimeProvider` + `DispatcherTimerFactory` cooperative clock
  feasibility audit in MotionCatalyst (`grep 'new DispatcherTimer'` expected
  to surface tens to hundreds of raw uses).
- Spike S-5: `ValueChangedEventManager` weak-event pattern for watched-DP
  change tracking — leak-free over 1e6 bump cycles.

Gate: all five spikes close with documented results before M1 starts.

**M1 Foundation**:
- `dotnet build` green across the full matrix.
- `SnoopAgent.StartCoLocated` runs against sample app, stdio round-trip.
- `Console.Out` takeover verified: a smoke test with `Console.WriteLine("junk")`
  in `App.OnStartup` does not corrupt MCP framing.
- `WpfInternals` accessor test suite: every internal-API accessor lands on
  every supported runtime; startup self-test gates session.

**M2 Input determinism (L0 / L1 / L2)**:
- Separate tools `wpf_execute_command`, `wpf_set_text_value` (L0 shortcuts).
- `wpf_click` defaulting to L1 AutomationPeer, `wpf_type_text` to L3 (via L1
  fallback if L3 unavailable at session level).
- Conformance test: on a sample app exercising both bound-command and
  peer-only paths, every input tool invokes the correct strategy.

**M3 Deterministic sync**:
- Tree versioning wired, works without debugger, benchmarked.
- `wpf_wait_for_property` (flat) and `wpf_wait_until_predicate` (DSL)
  end-to-end.
- Predicate DSL `$version: 1` enforced; fuzz-tested.
- Long-poll variant and `wpf_poll_changes` for non-SSE clients.
- Virtual clock via `ITimeProvider` + `DispatcherTimerFactory` — documented
  opt-in with cooperation requirement.

**M4 Input fidelity (L3 / L4)**:
- `InputManagerStrategy` with correct `DispatcherPriority.Send`.
- `SendInputStrategy` gated behind `MaxFidelity` + `reason` audit.
- Full chord-parser for `wpf_send_keys`.
- `wpf_drag`, `wpf_hover`, `wpf_scroll`, `wpf_right_click`, `wpf_double_click`.
- Focus gesture, text-composition (IME) input.

**M5 Semantic navigation**:
- `wpf_find_by_viewmodel` with short-name and full-name matching.
- `wpf_trace_command`, `wpf_resolve_binding` with full evaluation trace.
- `wpf_inspect_viewmodel` (new — discovery aid).
- `wpf_trace_style`, `wpf_trace_template` — style/template resolution chain.
- `wpf_coverage_commands` — find unbound ICommand references.

**M6 Capture**:
- `ISnoopVisualCapture` hook, packed-BGRA contract.
- `wpf_capture_region` with window-bounds clamp.
- `wpf_visual_diff` — ΔE, SSIM, MobileCLIP perceptual.
- `wpf_visual_diff_update` with operator confirmation flow.
- Out-of-band binary channel (shared memory / blob-ref fallback to inline).

**M7 Recording and replay**:
- HMAC-signed recording blobs.
- PID + start-time identity check on replay.
- Time-travel replay (step backwards through versions).
- Causal lineage graph per recording.
- Continuous passive ring-buffer mode.

**M8 Observability**:
- OpenTelemetry spans per tool call with full attributes.
- ETW event correlation for Dispatcher work.
- Chrome trace JSONL export.
- Accessibility compliance scoring tool.

**M9 Multi-agent coordination**:
- Session fan-out: >1 concurrent MCP client per app, read-only observer role.
- Driver arbitration (cooperative locks, optional barge-in with audit).
- Cross-session recording annotations.

**M10 Live edit mode**:
- Runtime XAML snippet injection.
- Style / template hot-replace.
- Scope control (element, window, application).
- Reverts on session end.

**M11 Release**:
- NuGet publish: `SnoopWPF.Agent.Automation` (core),
  `SnoopWPF.Agent.Automation.Skia` (perceptual), `SnoopWPF.Agent.Observability`,
  `SnoopWPF.Agent.MultiAgent`, `SnoopWPF.Agent.LiveEdit`.
- Full integration-test suite green on Windows Server 2022, Windows 11.
- Accessibility scoring baseline published.
- Migration guide for v3 and FlaUI consumers.
- Third-party injection mode documented.

### 4.2 Always-on gates

Beyond milestones, permanently enforced:

- **Zero `Thread.Sleep` / `Task.Delay` in any tool's success path**. CI scanner.
- **Every subsystem has a fuzz test**. Predicate evaluator, path handlers,
  recording deserializer, chord parser — all fuzzed.
- **Roslyn analyzer** forbids `Console.Write*` in co-located entrypoints.
- **`UnsafeAccessor` accessors** each have a runtime startup self-test.
- **Redaction coverage**: unit tests assert every documented redaction
  keyword triggers across every tool that reads values.

---

## 5. Architecture

### 5.1 Philosophy

Five architectural commitments:

1. **Honest naming**. A `click` is a click. A shortcut is a shortcut. No tool
   name describes a different code path than the caller would infer.
2. **Session-scoped policy**. Mode, fidelity ceiling, redaction policy,
   recording config, multi-agent role — all bound at session creation, not
   per-call. A tool call is a *request within an already-constrained policy
   envelope*.
3. **Event-driven by default**. Polling is a degradation, not a design.
4. **Contract-first coupling**. Every cross-subsystem dependency goes through
   a versioned interface in `SnoopWPF.Agent.Contracts`.
5. **Observable everywhere**. Every tool call, every state change, every
   strategy selection, every fidelity downgrade is a structured event.

### 5.2 Four integration modes

| Mode | Who hosts MCP | Transport | Max fidelity | When |
|------|---------------|-----------|--------------|------|
| **Co-located** | Target WPF app | stdio (primary), named pipe | L4 | Owned apps; best performance, fidelity, determinism. |
| **NuGet in-process** | Target WPF app | loopback TCP (MCP over HTTP/SSE) | L3 | Owned apps where `--mcp-stdio` flag can't be added; existing app entrypoint. |
| **Injection** | External `snoop-mcp.exe` | stdio external + pipe to target | L1 | Third-party / closed-source apps. |
| **Shared memory** | Target WPF app | shared-memory ring buffer + coordination pipe | L4 | High-throughput scenarios (subscriptions at 1 kHz, video frame capture); same process as co-located but zero-copy. |

Capability cap is applied at session creation as a policy object and cannot
be raised per-call. This collapses v4's matrix into a policy-with-hints model.

### 5.3 Co-located mode detail

The target WPF app hosts the MCP server on its own Dispatcher thread when
launched with `--mcp-stdio` (or configured in code via `SnoopAgent.StartCoLocated`).

#### 5.3.1 Boot sequence

```
MotionCatalyst.exe --mcp-stdio --headless
  1. Parse args: detect --mcp-stdio.
  2. Before ANY other initialization, replace Console.Out with a sink
     that discards (§5.3.3) and redirect stderr to a log file.
  3. Install UnsafeAccessor startup self-test. If any accessor misses,
     downgrade the session's MaxFidelity cap and log.
  4. Ensure a PresentationSource exists — create a hidden message-only
     HwndSource if none does (§5.3.2).
  5. Start the Dispatcher if not already.
  6. Register IInputEventSource, ITreeVersionSource, IDispatcherTimerFactory,
     ITimeProvider with the DI container.
  7. SnoopAgent.StartCoLocated:
       - Reads SnoopAgentOptions (EnableAutomation, EnableMutation,
         MaxFidelity, redaction, recording, multi-agent, observability).
       - Constructs SnoopInspector and wires up strategies.
       - Opens MCP server on stdin/stdout-as-discard-sink's-original-stdout
         (the real FD is held by SnoopAgent).
  8. App.OnStartup completes normally.
```

#### 5.3.2 Hidden `HwndSource` precondition

L2/L3 input requires a valid `PresentationSource` on the target visual. In
fully headless mode where no window is ever shown, no `HwndSource` exists and
`PresentationSource.FromVisual` returns null for every element.

The co-located agent resolves this by creating a **message-only hidden
`HwndSource`** at init if none exists. The hidden source is 1×1, `WS_POPUP`,
off-screen, and registered as a valid `PresentationSource`. Visual elements
that are not explicitly parented to another source are reparented to this
hidden source on first access.

Headless `PresentationSource` management is a first-class subsystem, not an
ad-hoc fix. `SnoopWPF.Agent.HeadlessSource` owns it.

#### 5.3.3 `Console.Out` takeover

**Hard takeover, not recommendation.** First statement of `SnoopAgent
.StartCoLocated`:

```csharp
var realStdout = Console.OpenStandardOutput();
Console.SetOut(TextWriter.Null);      // Any future Console.WriteLine → void
Console.SetError(new FileSinkTextWriter(options.StderrLogPath));
_mcpStream = realStdout;              // MCP owns the real FD
```

A Roslyn analyzer (`SWPF0001`) fires at build time in consumer projects
linked against `SnoopWPF.Agent.Automation.Analyzers` on any
`Console.Write*` call in an assembly marked `[assembly: SnoopMcpEntrypoint]`.
Third-party libraries cannot be analyzer-gated; for those we instead inject
a `TextWriter.Null` at app-domain init and provide an optional module-load
prohibition list (`SnoopAgentOptions.ProhibitConsoleFrom: string[]`).

### 5.4 Session-scoped policy

A session is created when an MCP client connects and torn down when it
disconnects. The session holds:

- **Mode**: one of the four. Derived from how the agent was started, not
  caller-controllable.
- **MaxFidelity**: the ceiling of allowed input tiers. Default L2 for NuGet,
  L4 for co-located and shared-memory, L1 for injection.
- **EnableAutomation / EnableMutation**: both default false; opt-in at
  session creation.
- **RedactionPolicy**: keyword list + structural redaction rules (§11.2).
- **RecordingPolicy**: off / passive ring-buffer / active.
- **MultiAgentRole**: driver / observer / arbiter.
- **ObservabilityConfig**: OTEL endpoints, ETW provider IDs, Chrome-trace
  output path.
- **VirtualClockMode**: off / injected-via-TimeProvider / injected-with-IL-rewrite.

Per-call parameters (including `fidelity`) are **hints within the session's
allowed range**, never escalations of it. Out-of-range hints return
`FidelityCapExceeded` with the session cap in the error body.

### 5.5 Project structure

```
Snoop.sln
│ ...v3 projects retained...
│
├── SnoopWPF.Agent.Contracts/              EXTENDED (netstandard2.0)
│     NEW contracts: IInputEventSource, ITreeVersionSource,
│     IDispatcherTimerFactory, ITimeProvider shim interfaces,
│     VersionedPredicateEnvelope, Observability surface types.
│
├── SnoopWPF.Agent.HeadlessSource/          NEW (net6/8/9-windows, UseWpf)
│     Owns the hidden HwndSource; PresentationSource registration.
│
├── SnoopWPF.Agent.Input.Deterministic/     NEW (net6/8/9-windows, UseWpf)
│     L0/L1/L2 strategies. IDeterministicInputStrategy interface.
│     No threading exotica; all run inside Dispatcher.InvokeAsync.
│
├── SnoopWPF.Agent.Input.Probabilistic/     NEW (net6/8/9-windows, UseWpf)
│     L3/L4 strategies. IProbabilisticInputStrategy interface.
│     InputManagerStrategy, SendInputStrategy.
│     UnsafeAccessor shims (WpfInternals) live here.
│
├── SnoopWPF.Agent.Query/                   NEW (merged Sync + Semantic)
│     Tree versioning, predicate DSL (versioned),
│     wait/subscribe/poll, virtual clock, ViewModel walker,
│     command reverse index, binding resolver.
│
├── SnoopWPF.Agent.Capture/                 NEW (net6/8/9-windows)
│     ISnoopVisualCapture dispatch, composite render,
│     baseline store, ΔE / SSIM / MobileCLIP diff.
│
├── SnoopWPF.Agent.Recording/               NEW (net6/8/9-windows)
│     HMAC-signed recording, time-travel playback,
│     ring-buffer mode, causal lineage.
│     Consumes IInputEventSource + ITreeVersionSource from Contracts.
│     DOES NOT depend on Input.* or Query.* projects.
│
├── SnoopWPF.Agent.Observability/           NEW (net6/8/9-windows)
│     OTEL spans, ETW correlation, Chrome-trace export,
│     accessibility compliance scoring.
│
├── SnoopWPF.Agent.MultiAgent/              NEW (net6/8/9-windows)
│     Session fan-out, role arbitration, federation protocol.
│
├── SnoopWPF.Agent.LiveEdit/                NEW (net6/8/9-windows)
│     Runtime XAML injection, scoped template replacement.
│
├── SnoopWPF.Agent.Automation/              NEW (net8.0-windows)
│     Top-level package. Hosts InputStrategySelector (mode-aware),
│     MCP tool types for all subsystems, SnoopAgent.StartCoLocated,
│     integration with DI.
│
├── SnoopWPF.Agent.Automation.Analyzers/    NEW (netstandard2.0)
│     Roslyn analyzers: SWPF0001 (no Console.Write*),
│     SWPF0002 (no Thread.Sleep in tool handlers),
│     SWPF0003 (IInputStrategy must live in Input.* package).
│
├── SnoopWPF.Agent.Shim.FlaUI/              NEW (net8.0-windows)
│     Migration shim. Re-implements the most-used FlaUI surface
│     against MCP tools. ~1,500 LOC target. Lets consumers migrate
│     scenario-by-scenario.
│
├── SnoopWPF.Agent.Tests/                   EXTENDED
├── SnoopWPF.Agent.IntegrationTests/        EXTENDED
├── SnoopWPF.Agent.FuzzTests/               NEW
├── SnoopWPF.Agent.PerformanceTests/        NEW
└── SnoopWPF.Agent.SampleApp/               EXTENDED
```

### 5.6 Dependency graph (key edges)

```
Contracts ──┬── Input.Deterministic
            ├── Input.Probabilistic ── HeadlessSource
            ├── Query
            ├── Capture
            ├── Recording            ── IInputEventSource / ITreeVersionSource
            ├── Observability
            ├── MultiAgent
            └── LiveEdit

Automation ──┬── Input.Deterministic
             ├── Input.Probabilistic
             ├── Query
             ├── Capture
             ├── Recording
             ├── Observability
             ├── MultiAgent
             ├── LiveEdit
             ├── HeadlessSource
             └── ModelContextProtocol
```

Recording's dependency on `IInputEventSource` and `ITreeVersionSource` from
Contracts (not from the concrete Input/Query projects) is the key v5 fix for
v4's hidden coupling. Contract-first.

### 5.7 Transport and out-of-band channels

**Control plane** (MCP over stdio, named pipe, or loopback TCP):

- JSON-RPC requests and responses.
- Server-sent notifications for subscriptions (when client supports).
- Strict per-message size cap (4 MiB default, configurable up to 16 MiB).

**Data plane** (out-of-band binary):

- **Shared memory** (`Memory-Mapped File`) for screenshots, recording blobs,
  and large predicate results. Ring-buffered, producer-consumer coordinated
  via semaphores. Works only in co-located and shared-memory modes.
- **Blob-ref fallback**: tool returns `{ "blob": "blob://sess-42/img-17" }`;
  client calls `wpf_fetch_blob(blob)` to retrieve bytes. Works in any mode.
- **Inline base64 fallback**: legacy MCP behaviour; enabled if client doesn't
  negotiate blob-ref support. Works in any mode.

Negotiation: at session init, client sends `capabilities.blobTransport` with
preference order. Server picks the highest-priority supported option.

### 5.8 Multi-dispatcher (first-class)

WPF apps with more than one Dispatcher (e.g., separate UI threads for
tool windows) are native v5 citizens, not an optional M-dispatcher
story.

- Node IDs encode dispatcher index: `"{dispIdx}:{counter}"`.
- `wpf_get_session_info` returns `dispatchers: [{ id, threadId, windowNodeIds }]`.
- Every tool takes `dispatcherId?` (optional; defaults to dispatcher that
  owns the target node).
- Cross-dispatcher input is **linearized** through a per-app mutex;
  `wpf_click` on a node owned by Dispatcher 1 cannot interleave with
  `wpf_set_property` on Dispatcher 0.
- Tree versioning is per-dispatcher; session version is the monotonic
  maximum.

### 5.9 Threading model

All WPF object access via Dispatcher, as in v3/v4. v5 additions:

1. **Input strategies split by determinism**: L0/L1/L2 run synchronously
   inside `Dispatcher.InvokeAsync`; L3 uses `DispatcherPriority.Send`
   (not `Input` — that's wrong per the WPF-agent review); L4 runs on
   transport thread, uses `SendInput`, then gates completion on tree-version
   change (not `ApplicationIdle`).
2. **Subscription debounce**: 16 ms (one-frame) coalescing window.
3. **Virtual clock's two tiers**: `ITimeProvider` cooperative (no IL rewrite)
   and IL-rewrite tier (for `DispatcherTimer` instances the consumer can't
   control). IL rewrite is opt-in per app, via `[assembly: SnoopVirtualClockAllowed]`.
4. **Multi-agent driver lock**: a single driver session at any moment per
   app; other sessions are observer-only. Driver can be swapped via MCP
   tool `wpf_multi_agent_yield`.

### 5.10 Thesis diagram

```
  Claude Code
       │ stdio MCP  (or loopback TCP / named pipe / shared memory)
       ▼
  SnoopAgent (in MotionCatalyst process)
       │
       ├── Session Policy  (mode-scoped, capability cap, redaction, audit)
       │
       ├── InputStrategySelector
       │       ├── Deterministic (L0/L1/L2) - IDeterministicInputStrategy
       │       └── Probabilistic (L3/L4)   - IProbabilisticInputStrategy
       │
       ├── Query Subsystem
       │       ├── Tree version tracker (multi-source; no VisualDiagnostics)
       │       ├── Predicate DSL v1 (versioned, typed)
       │       ├── Wait / Subscribe / Poll / LongPoll
       │       └── Virtual clock (ITimeProvider + IL-rewrite tier)
       │
       ├── Semantic Query
       │       ├── ViewModel walker
       │       ├── Command reverse index
       │       └── Binding resolver
       │
       ├── Capture (ΔE / SSIM / MobileCLIP)
       │
       ├── Recording (HMAC, time-travel, ring-buffer)
       │
       ├── Observability (OTEL / ETW / Chrome trace)
       │
       ├── MultiAgent (fan-out, arbitration)
       │
       └── LiveEdit (XAML injection, scoped)

  Out-of-band data plane: shared-memory / blob-ref / inline fallback
```

---

## 6. Input Simulation Subsystem

### 6.1 Honest tier naming

Five tiers, each exposed through tools named for what they *actually do* —
not behind a `fidelity` parameter on a generic `click`.

| Tier | Physical meaning | Tool name(s) |
|------|------------------|--------------|
| **L0 Semantic shortcut** | Call `ICommand.Execute` / set DP directly. Bypasses UI chrome. | `wpf_execute_command`, `wpf_set_text_value`, `wpf_set_check_state`, `wpf_select_item` |
| **L1 Automation peer** | Invoke `UIElementAutomationPeer` patterns. Same as UIA. | `wpf_click` (default), `wpf_toggle`, `wpf_expand_collapse`, `wpf_invoke_pattern` |
| **L2 Routed event** | `element.RaiseEvent(...)` with synthesized args. | Opt-in via `advanced.tier = "event"`; not a default for any tool. |
| **L3 Input manager** | `InputManager.Current.ProcessInput` with `RawInputReport`. | `wpf_type_text` (default), `wpf_send_keys`, `wpf_drag`, `wpf_hover`, `wpf_scroll` |
| **L4 Hardware** | Win32 `SendInput` to foreground window. | Opt-in via `advanced.tier = "hardware"` with audit `reason`. |

Key change from v4: **L0 is a separate family of tools**, not a disguised
default behind `click`. This eliminates the "test passes while bypassing the
input path" failure mode.

### 6.2 Strategy interfaces

Split per Architecture agent's Critical finding.

```csharp
// Deterministic: completes synchronously inside Dispatcher.InvokeAsync.
// Semantics is deterministic: success or typed failure, no races.
public interface IDeterministicInputStrategy
{
    DeterministicInputResult Invoke(
        DependencyObject target,
        InputIntent intent,
        CancellationToken ct);
}

// Probabilistic: completes asynchronously. Gated on tree-version change
// or timeout. Real user input can interleave.
public interface IProbabilisticInputStrategy
{
    Task<ProbabilisticInputResult> InvokeAsync(
        DependencyObject target,
        InputIntent intent,
        TreeVersionGate completionGate,
        CancellationToken ct);
}

// Selector returns discriminated union; caller must handle the distinction.
public readonly struct InputInvocation
{
    public readonly InputInvocationKind Kind;
    public readonly DeterministicInputResult Deterministic;
    public readonly Task<ProbabilisticInputResult> Probabilistic;
}
```

`InputStrategySelector` lives in `SnoopWPF.Agent.Automation` (not `Input.*`),
because selection requires knowledge of session-scoped policy (mode cap,
EnableAutomation, MaxFidelity, profile-guided cache).

### 6.3 Auto-select cascade

```
wpf_click(nodeId):
  1. Resolve node and element type.
  2. Consult profile-guided cache (§6.4). If this element signature has a
     recent successful fidelity, use it.
  3. Else apply cascade:
     a. If session MaxFidelity < L1, fail with FidelityCapExceeded.
     b. If element has IInvokeProvider automation peer → L1.
     c. Else if element has geometry in visual tree → L3.
     d. Else if element is in tree but not hit-testable → L2 (with warning).
     e. Else → FidelityNotAvailable.
  4. Run strategy. On success, update profile cache.
  5. On failure with `auto-fallback: true`, try next cascade step.
```

**No auto-escalate / auto-fallback conflation**. Two separate hints in
`advanced`:
- `advanced.autoFallback: bool` — retry with next cascade step on strategy
  failure (default false).
- `advanced.tier: string?` — override cascade entirely.

### 6.4 Profile-guided fidelity cache

An in-memory cache keyed by `(elementType, xamlName, automationId,
dataContextTypeName)` maps to a recent-successful fidelity tier. Updated
after every successful invocation. LRU-bounded (default 10k entries).
Persisted optionally (opt-in) to `%LOCALAPPDATA%\SnoopWPF\profiles\{app}.json`
for cross-session learning.

Observability: each cache hit emits a `snoop.input.cache_hit` OTEL span
attribute; misses emit `snoop.input.cache_miss`.

### 6.5 Complete tool inventory (input)

**L0 family (explicit shortcuts)**:
- `wpf_execute_command(nodeId, commandPropertyName?, parameter?)` — invokes
  `ICommand` with `CanExecute` gate.
- `wpf_set_text_value(nodeId, text, commitBinding?)` — sets `Text` /
  `PasswordBox.Password` via SetValue; optionally forces binding commit.
- `wpf_set_check_state(nodeId, state)` — sets `IsChecked`.
- `wpf_select_item(nodeId, criterion)` — sets `IsSelected` / `SelectedItem`.

**L1 family (automation peer)**:
- `wpf_click(nodeId, advanced?)` — L1 default.
- `wpf_toggle(nodeId, advanced?)` — `IToggleProvider`.
- `wpf_expand_collapse(nodeId, state, advanced?)`.
- `wpf_invoke_pattern(nodeId, patternName, parameters, advanced?)` — generic.

**L3 family (input manager)**:
- `wpf_type_text(nodeId, text, advanced?)` — L3 default.
- `wpf_send_keys(nodeId?, chord, advanced?)` — focus + key.
- `wpf_drag(fromNodeId, toNodeId | toPoint, advanced?)`.
- `wpf_hover(nodeId, advanced?)`.
- `wpf_scroll(nodeId, direction, amount?, advanced?)`.
- `wpf_right_click(nodeId, advanced?)`.
- `wpf_double_click(nodeId, advanced?)`.
- `wpf_focus(nodeId)`.

**L4 family** (explicit opt-in):
- Available through `advanced.tier = "hardware"` on any L3 tool, with
  required `advanced.reason` string (audit).

Total: **15 input tools**.

### 6.6 Internal API access

Appendix A holds the corrected `UnsafeAccessor` signatures (single-overload
`InputReportEventArgs` constructor taking `InputReport` base). Startup
self-test runs on agent init; any missing accessor downgrades session
`MaxFidelity`.

Runtime version check: before any accessor call, assert the loaded
`PresentationCore` assembly matches the expected version tag. In injection
mode, if the target's `PresentationCore` differs, L3/L4 hard-disable with
`IncompatibleRuntime` error.

---

## 7. Capture Subsystem

### 7.1 Pipeline

```
wpf_capture_region / wpf_capture_screenshot / wpf_visual_diff
  │
  ├── Dispatch by element type (ISnoopVisualCapture registry)
  │     │
  │     ├── Registered handler? → call handler, get packed BGRA top-down.
  │     └── None? → fall through.
  │
  ├── RenderTargetBitmap path (default for pure WPF).
  │
  └── PrintWindow path (last resort; window-level capture only).
```

### 7.2 `ISnoopVisualCapture` contract

```csharp
public interface ISnoopVisualCapture
{
    /// <summary>
    /// Capture visual as packed BGRA top-down bytes.
    /// - width * height * 4 == bgra.Length.
    /// - Row 0 is the top of the visual.
    /// - No padding bytes within rows.
    /// If the underlying surface uses padded rows (DXGI RowPitch != width*4),
    /// the implementation MUST copy row-by-row into a packed buffer.
    /// </summary>
    bool TryCapture(
        Visual visual,
        int maxWidth,
        int maxHeight,
        out byte[] bgra,
        out int width,
        out int height);
}
```

Row-pitch alignment is an explicit contract term (WPF agent finding).

### 7.3 Visual diff

Three comparators, user-selectable:

- **ΔE2000** — per-pixel colour difference. Fast (~20 ms for 1 MP).
- **SSIM** — structural similarity. Better for layout diffs.
- **MobileCLIP** — perceptual embedding distance. Catches "the button's
  icon changed" even with identical pixel distribution. Uses ONNX Runtime
  with MobileCLIP-S0; ~60 ms for 1 MP on CPU, 10 ms on DirectML GPU.

`wpf_visual_diff(nodeId, baselineKey, comparator?, tolerance?)` — defaults
to SSIM.

Baselines stored at `%LOCALAPPDATA%\SnoopWPF\baselines\{appName}\{key}.png`
with owner-only ACL. TTL configurable (default 90 days); explicit
`wpf_visual_diff_prune(olderThanDays)` tool for hygiene.

---

## 8. Query Subsystem (Sync + Semantic merged)

### 8.1 Why merged

Both sit on `Contracts` + `Snoop.Core`, read-only, always consumed together
by every non-trivial automation flow. No isolation benefit to splitting.
Architecture agent's recommendation adopted.

### 8.2 Tree versioning (correct design)

Four change-source feeds, combined into a single `ITreeVersionSource`:

1. **Per-node `FrameworkElement.Loaded` / `Unloaded`** — covers element add /
   remove in XAML-driven subtrees. Subscribed lazily on first access.
2. **`Panel.InternalChildren.CollectionChanged`** — covers programmatic
   `Children.Add/Remove` on panels that use `INotifyCollectionChanged`.
   Requires reflection to access internal `Children` field where not public.
3. **`CompositionTarget.Rendering`** — frame-tick walk over registered roots
   to catch everything else. Budget-limited to 200 µs per frame; skips if
   no changes detected via shallow hash of `GetChildrenCount(root)`.
4. **Explicit `Bump()`** — consumers (inside or outside the agent) can
   signal a mutation the change detector can't see (`ItemsSource` rebind
   on a virtualizing collection, custom `FrameworkElement` subclass with
   non-visual child semantics).

Change detection does **not** depend on
`VisualDiagnostics.VisualTreeChanged` (debugger-only).

### 8.3 Watched property tracking (leak-free)

Per WPF agent finding: `DependencyPropertyDescriptor.AddValueChanged` leaks.
v5 uses **`ValueChangedEventManager`** weak-event pattern:

```csharp
public static void Watch<T>(DependencyObject target, DependencyProperty dp,
                             Action<DependencyObject, T, T> handler)
    where T : notnull
{
    // WeakEventManager subclass; handler held weakly.
}
```

Custom `WeakEventManager` subclass per watched DP keeps no strong refs to
the subscriber.

### 8.4 Wait primitives

Three levels, all available:

- **`wpf_wait_for_property(nodeId, propertyName, expectedValue, timeoutMs?)`** —
  flat, agent-friendly, 90% case. No DSL.
- **`wpf_wait_until(predicate, rootNodeId?, minVersion?, timeoutMs?)`** —
  full DSL (§8.5). Power-user path.
- **`wpf_wait_for_changes(sinceVersion, rootNodeId?, timeoutMs?)`** —
  long-poll: blocks until any change past `sinceVersion`, returns changes.
  For clients without SSE.

And non-blocking:

- **`wpf_poll_changes(sinceVersion, rootNodeId?)`** — returns immediately;
  clients implement their own polling loop if they prefer.

### 8.5 Predicate DSL v1 (versioned, typed)

Every predicate object carries `"$version": 1`:

```json
{
  "$version": 1,
  "$and": [
    { "property": "IsVisible", "op": "eq", "value": true },
    { "property": "Text", "op": "matches", "value": "^Recording$" },
    { "automationId": "StartButton" }
  ]
}
```

#### 8.5.1 Operators

- `eq`, `ne`, `gt`, `lt`, `ge`, `le` — strict type-checked comparison.
- `contains`, `startsWith`, `endsWith` — string ops on values up to 64 KB;
  oversized values short-circuit to `PredicateValueTooLarge`.
- `matches` — regex with `NonBacktracking + ExplicitCapture`, pattern cap
  1000 chars, value cap 64 KB.
- `in` — membership against array up to 128 elements.
- `isNull`, `isNotNull`.

#### 8.5.2 Compositors

- `$and`, `$or`, `$not`.
- Nesting depth cap: 16.
- Total expression-node cap: 256 (prevents DoS via deep structures).

#### 8.5.3 Temporal operators (new)

Goes beyond SOTA for UI automation predicate languages:

- `"$after": { "$within": "500ms", "match": { ... } }` — matches only if
  the condition has been true for *at least* the window.
- `"$transitioned": { "from": <predicate>, "to": <predicate> }` — matches
  on the exact transition, not the static state.
- `"$stable": { "$for": "200ms", "match": { ... } }` — matches only after
  the condition has held without any tree version bump for the duration.

Temporal state tracked per subscription/wait, not globally; cleaned up on
subscription TTL.

#### 8.5.4 Property whitelist

Callers can configure `SnoopAutomationOptions.PredicatePropertyWhitelist` to
restrict evaluable properties. Defaults to the watched-property set (§7.1).

#### 8.5.5 Evaluation quota

Per-evaluation CPU budget (default 10 ms); exceed → `PredicateTimeout`.
Per-subscription wall clock (1 kHz maximum evaluation rate).

### 8.6 Subscription delivery (three-mode)

- **SSE** via MCP notifications — for clients with async notification
  handlers. Highest throughput, lowest latency.
- **Long poll** via `wpf_wait_for_changes` — HTTP-style blocking call.
- **Explicit poll** via `wpf_poll_changes` — caller-driven loop.

Server negotiates preferred channel at session init; all three are always
available. Non-SSE clients are not second-class: their round-trip cost is
one `wait_for_changes` call per logical event, which is bounded by the
event rate itself.

### 8.7 Virtual clock (realistic)

Three cooperation tiers:

**Tier A — `ITimeProvider` adoption** (most portable, recommended):
- Consumer uses `TimeProvider.GetUtcNow()` / `CreateTimer()` everywhere.
- `SnoopAgent.StartCoLocated` installs a manipulable provider.
- `wpf_advance_time(ms)` advances; all `TimeProvider`-driven timers fire.

**Tier B — `DispatcherTimerFactory` injection** (for unconverted code):
- Consumer uses `IDispatcherTimerFactory` abstraction instead of `new
  DispatcherTimer()` directly.
- Factory returns `DispatcherTimer` wrappers the agent controls.

**Tier C — IL rewrite** (for the last 10%):
- Opt-in via `[assembly: SnoopVirtualClockAllowed]`.
- Agent post-processes loaded assemblies at startup, rewriting
  `new DispatcherTimer(...)` construction sites to route through the factory.
- Uses `System.Reflection.Emit` or `AsmResolver` depending on framework.
- Honest about the risk: IL rewrite can fail on obfuscated assemblies;
  tier A/B recommended for production.

v4's plan of hooking `DispatcherTimer._timeManager` is retracted entirely —
the field doesn't exist (WPF agent).

### 8.8 Semantic navigation

**`wpf_find_by_viewmodel(viewModelType, propertyPath?, value?, rootNodeId?,
maxResults?)`**:
- Short-name matching default (`"SessionVm"` matches `"MyApp.VM.SessionVm"`).
- Full-name matching via `fullyQualified: true`.
- Redaction: `propertyPath` checked against the redaction keyword list
  before resolution; matches return `PropertyRedacted` rather than
  exposing values indirectly.
- Evaluation cap: 64 KB per resolved value; same DoS protection as
  predicate DSL.

**`wpf_trace_command(commandPath? | commandName?)`** — reverse index from
`ICommand` to bound elements.

**`wpf_resolve_binding(nodeId, propertyName)`** — full evaluation trace
(every path segment, intermediate types, null-states, converter, final
value, validation errors).

**`wpf_inspect_viewmodel(nodeId)`** (new discovery aid) — dumps the
DataContext type, its public properties, and a shallow snapshot of values
(redacted per policy). Lets callers discover the ViewModel surface without
reading source.

**`wpf_trace_style(nodeId)` / `wpf_trace_template(nodeId)`** — resolves
style / template hierarchy, trigger chain, BasedOn chain. Full diagnostic.

**`wpf_coverage_commands()`** — finds unbound `ICommand` references (e.g.
elements with `Command={Binding ...}` where resolution is null). Useful
for dead-command detection.

---

## 9. Recording and Replay Subsystem

### 9.1 HMAC-signed recordings

Every recording blob carries:

```
{
  "$version": 1,
  "sessionToken": "<opaque>",
  "processIdentity": {
     "pid": 12345,
     "processStartTime": "2026-04-15T10:12:34.567Z",
     "executablePath": "C:\\...\\MotionCatalyst.exe",
     "executableHash": "<sha256>"
  },
  "events": [ ... ],
  "signature": "<hmac-sha256-over-canonical-json>"
}
```

The HMAC key is derived from the session token (generated at
`wpf_record_start` time and stored in owner-only temp file). `wpf_replay`
verifies the signature before any event executes. Unsigned or
signature-invalid blobs are refused.

### 9.2 Path constraints

`wpf_replay({ blob? | path? })`:
- If `blob`, content is verified directly.
- If `path`, must resolve under the configured recordings root
  (`%LOCALAPPDATA%\SnoopWPF\recordings` by default) — path traversal
  rejected; symlinks followed but still checked under root after
  resolution.

### 9.3 Cross-process identity check

v4's "PID match" is defeated by PID reuse. v5 uses:

- **Process start time** from `GetProcessTimes` (Windows kernel-supplied;
  unique per process lifetime).
- **Executable path + hash** — an attacker's replacement binary at the
  same path fails the hash check.

Cross-process replay allowed only with `allowCrossProcess: true` AND
matching `executablePath` + `executableHash`. PID alone is not an identity.

### 9.4 Time-travel replay (new)

Recordings contain per-event tree-version snapshots. `wpf_replay_step`
advances one event at a time; `wpf_replay_rewind(toVersion)` restores the
UI to a past state by re-executing events up to the target version.

Because replay runs against live application state (not a snapshot of the
process), "rewind" is semantically "re-run to this point." For
true state rewind, see §9.6 (state machine induction).

### 9.5 Continuous passive ring-buffer (new)

`SnoopAgent.StartCoLocated` with `RecordingPolicy.PassiveRingBuffer(size:
10_000_events)` silently captures every tool call + tree-version bump into
a ring buffer. Never written to disk unless triggered.

`wpf_recording_snapshot()` freezes the current ring and returns the buffer
as a signed recording blob. Useful for "capture the 60 seconds before the
bug happened."

### 9.6 Causal lineage graph (new)

Every recorded event carries:
- `inputs`: the tool call that triggered it (if any).
- `effects`: the tree-version bumps that followed within a bounded window
  (default 500 ms).
- `edges`: derived "A caused B" relationships (best-effort; temporally
  ordered, not formally proven).

The recording blob is therefore a directed graph, not a sequence. Enables
queries like: "which input was responsible for the tree change that
exposed bug X?"

### 9.7 State machine induction (new, beyond SOTA)

`wpf_induce_state_machine(recordingBlob, abstractionLevel?)` — builds a
Mealy machine from recorded sessions. States = equivalence classes of
tree state (by abstraction policy); transitions = recorded inputs.

Used by:
- **Property-based test generation** — Hypothesis / FsCheck-style random
  walks over the induced machine, validating invariants hold at every
  reachable state.
- **Coverage reporting** — which states / transitions are observed; which
  predicted-but-never-reached.
- **Regression detection** — compare induced machines across builds; alert
  on added / removed transitions.

Requires abstraction policy input (which DPs define equivalence classes);
ships with sensible defaults (`IsEnabled`, `Visibility`, `Text`).

---

## 10. Observability Subsystem

### 10.1 OpenTelemetry spans

Every MCP tool call creates a span:
- Name: `snoop.{tool_name}`.
- Attributes: `snoop.mode`, `snoop.fidelity_chosen`, `snoop.node_id`,
  `snoop.tree_version_before`, `snoop.tree_version_after`,
  `snoop.strategy_used`, `snoop.cache_hit`, `snoop.session_id`.
- Events inside the span: strategy downgrade, cache miss, fallback-polling
  trigger.

Spans export via OTLP/HTTP by default to `http://localhost:4318/v1/traces`
when an endpoint is configured.

### 10.2 ETW correlation

A custom ETW provider (`SnoopWPF-Agent`, known GUID) emits events for:
- Tool call start / end.
- Strategy invocation.
- Tree version bump (count, not full diff).
- Subscription fire.

Correlates with built-in WPF ETW events (`Microsoft-Windows-Wpf`,
`Microsoft-Windows-Win32k`) via activity IDs.

### 10.3 Chrome trace export

`wpf_export_trace(format: "chrome", path: "...")` writes a Chrome trace
JSONL of the session (tool calls, Dispatcher work, render frames). Viewable
in `chrome://tracing` or Perfetto for visual perf analysis.

### 10.4 Accessibility compliance scoring

`wpf_score_accessibility(rootNodeId?)` — walks the tree and scores:
- Proportion of interactive elements with `AutomationId`.
- Proportion with `AutomationProperties.HelpText`.
- Focus-cycle completeness (every focusable element reachable via
  keyboard).
- Colour contrast of text against background (from resolved brushes).
- Keyboard alternative coverage for pointer-only gestures.

Returns a weighted score 0–100 plus a per-element gap list.

---

## 11. Security Model

Every C/H/M/L finding from the security review is addressed here.

### 11.1 Strategy-level gating (addresses C2)

`EnableAutomation` and `EnableMutation` checks live in
`InputStrategySelector` at strategy construction time, not in tool
handlers. Any invocation through any tool (`wpf_click`, `wpf_set_property`
with `advanced.tier`, `wpf_execute_command`) passes the gate.

```csharp
public InputInvocation Select(Session session, InputIntent intent, ...)
{
    if (intent.Tier == InputTier.L0 && !session.EnableMutation)
        throw McpError.MutationDisabled();
    if (intent.Tier >= InputTier.L1 && !session.EnableAutomation)
        throw McpError.AutomationDisabled();
    if (intent.Tier > session.MaxFidelity)
        throw McpError.FidelityCapExceeded(session.MaxFidelity);
    ...
}
```

### 11.2 Structural redaction (addresses H1)

Redaction is now layered:

1. **Keyword redaction** (from v3) on property *names*.
2. **Structural redaction** (new): any tool parameter of semantic type
   `Credential`, `UserInput`, `SensitiveText` is redacted unless the
   caller sets `advanced.retainSensitive: true` AND the session has
   `AllowSensitiveRetention: true`.
   - `wpf_type_text`'s `text` parameter is `SensitiveText` by default.
   - `wpf_execute_command`'s `parameter` is `UserInput` when the
     command name contains a redaction keyword.
3. **Per-element opt-out** via `[Snoop.Retain]` attribute on the DP
   declaration (for consumer-owned DPs).

### 11.3 `find_by_viewmodel` redaction (addresses H2)

`propertyPath` resolved against the redaction keyword list before the
value is read. A matching path returns `PropertyRedacted`; no binary
search exfiltration possible.

### 11.4 Recording path constraints (addresses C1, M1)

- Recording blob files written under `%LOCALAPPDATA%\SnoopWPF\recordings`
  with owner-only ACL (addresses L1).
- `wpf_replay({ path })` resolves the path, verifies it's under the
  configured root, rejects symlinks escaping the root.
- HMAC signature verification (addresses C1).
- PID + process start time + executable hash identity (addresses M1).

### 11.5 Structured audit log (addresses H3)

Separate from stderr. Writes to `%LOCALAPPDATA%\SnoopWPF\audit\{session}.jsonl`
with owner-only ACL. Each entry:

```json
{
  "seq": 17,
  "timestamp": "2026-04-15T10:12:34.567Z",
  "session": "sess-abc",
  "tool": "wpf_send_keys",
  "tier": "hardware",
  "nodeId": "0:42",
  "reason": "UAC-elevation-test",
  "reasonSanitized": true,
  "hmac": "..."
}
```

`reason` capped at 256 chars, stripped of newlines/ANSI, HMAC over entry.

### 11.6 Injection-mode hard gates (addresses H4)

- `InputStrategySelector` refuses to construct L3/L4 strategies when
  `session.Mode == Injection`. Not a runtime check inside the strategy;
  refused at selection time.
- Runtime assertion: before any `UnsafeAccessor` call, verify the loaded
  `PresentationCore` assembly version matches the accessor's compile-time
  assumption. Mismatch → `IncompatibleRuntime`.

### 11.7 Predicate DSL hardening (addresses M2, plus)

- 64 KB cap on resolved string values (addresses M2).
- Property whitelist (§8.5.4).
- Per-evaluation CPU quota (§8.5.5).
- Regex `NonBacktracking + ExplicitCapture`.
- Nesting depth + expression-node caps (§8.5.2).
- `$version` required; unknown versions rejected.

### 11.8 Capture region clamp (addresses M3)

`wpf_capture_region` clamps the requested rect to the union of the target
app's window bounds. Rect extending outside returns `CaptureOutOfBounds`.
No cross-app PII leak.

### 11.9 Baseline store hygiene (addresses part of H1)

- Baselines stored at `%LOCALAPPDATA%\SnoopWPF\baselines\{app}\` with
  owner-only ACL.
- TTL default 90 days; `wpf_visual_diff_prune` tool.
- Opt-in "sensitive mode": baselines encrypted-at-rest with key derived
  from session token; unreadable after session end.

### 11.10 `EnableAutomation` documentation (addresses M4)

`SECURITY.md` explicitly states:

> `EnableAutomation: true` permits state mutation through the application's
> input pipeline. `EnableMutation: false` does not constrain state changes
> caused by legitimate user inputs simulated via L1/L3 strategies.
> Grant both flags as a pair; grant neither for pure inspection.

### 11.11 Virtual clock opt-in (addresses L2)

`wpf_advance_time` requires `SnoopAutomationOptions.AllowVirtualClock: true`.
Disabled by default even when `EnableAutomation: true`.

### 11.12 Multi-agent isolation

Multi-agent mode (§12) introduces a new trust axis. Each agent has its own
session token; the driver lock prevents concurrent mutations; observer
sessions cannot invoke input tools regardless of session flags.

---

## 12. Multi-Agent Coordination and Live Edit

### 12.1 Multi-agent coordination

Two or more MCP clients attach to the same agent concurrently.

**Roles**:
- `Driver` — sole role allowed to invoke L1–L4 input tools and mutations.
  One at a time, lock held.
- `Observer` — read-only; can call any query/inspection tool, cannot
  invoke input or mutation.
- `Arbiter` — can swap the driver (useful for hand-off in collaborative
  debugging). Rare role; explicit opt-in.

**Driver lock**:
- Acquired via `wpf_multi_agent_request_driver(reason?, timeoutMs?)`.
- Held until `wpf_multi_agent_yield()` or session end.
- Contention: at most one driver; other requests queued FIFO with timeout.
- Barge-in: `wpf_multi_agent_barge(justification)` — allowed only for
  Arbiter role; audit-logged.

**Cross-session annotations**: observers can attach read-only annotations
to any tool call the driver made, visible in recordings.

### 12.2 Session federation (new)

Multiple agents running in multiple WPF apps can form a federation: each
app's MCP surface is reachable from the others via pipe/loopback TCP.

Use case: an automation scenario spans two apps (main app + companion
tool). A single Claude session connects to both agents; tools route by
`processName` prefix.

Federation protocol is out-of-band from MCP; described in Appendix C.

### 12.3 Live edit mode

**`wpf_live_edit_style(nodeId, xamlSnippet)`** — replaces a target's
resolved style with a runtime-compiled XAML snippet. Scoped to the
element; revertible.

**`wpf_live_edit_template(nodeId, xamlSnippet)`** — same for
`ControlTemplate`.

**`wpf_live_edit_xaml(nodeId, xamlSnippet, scope?)`** — injects a child
visual under the node. Scope: `element` / `window` / `app`.

**`wpf_live_edit_revert(editId | all)`** — undoes a live edit. All edits
revert on session end; a crash leaves them persistent only if the
recording has `persistLiveEdits: true`.

Gated behind `EnableLiveEdit: true`. Audit-logged per edit. The XAML
snippet is parsed in a sandbox (`XamlXmlReader` with a restricted type
allowlist) before compilation.

---

## 13. MCP Tool Inventory (complete)

### 13.1 From v3 (retained, unmodified except `wpf_set_property`)

15 tools, all still present: `wpf_get_session_info`, `wpf_get_windows`,
`wpf_get_visual_tree`, `wpf_get_children`, `wpf_get_ancestors`,
`wpf_find_elements`, `wpf_inspect_element`, `wpf_get_properties`,
`wpf_set_property`, `wpf_get_binding_info`, `wpf_run_diagnostics`,
`wpf_get_resources`, `wpf_capture_screenshot`, `wpf_get_triggers`,
`wpf_get_behaviors`.

`wpf_set_property` gains optional `advanced.tier` (gated by §11.1).

### 13.2 Input (15 tools)

L0 (4): `wpf_execute_command`, `wpf_set_text_value`, `wpf_set_check_state`,
`wpf_select_item`.

L1 (4): `wpf_click`, `wpf_toggle`, `wpf_expand_collapse`,
`wpf_invoke_pattern`.

L3 (7): `wpf_type_text`, `wpf_send_keys`, `wpf_drag`, `wpf_hover`,
`wpf_scroll`, `wpf_right_click`, `wpf_double_click`, `wpf_focus` — and
counting `wpf_focus` as a utility = 7 distinct shapes, though semantically
8. Count as 7 tools since `wpf_focus` overlaps with `wpf_send_keys`'s
setup phase but is exposed separately.

(L4 is an advanced parameter, not distinct tools.)

### 13.3 Sync + Semantic (14 tools, was 8 in v4)

Sync (6): `wpf_wait_for_property`, `wpf_wait_until`, `wpf_wait_for_changes`,
`wpf_poll_changes`, `wpf_subscribe`, `wpf_unsubscribe`.

Utility (2): `wpf_pump_dispatcher`, `wpf_pump_until_idle`.

Clock (1): `wpf_advance_time`.

Semantic (5): `wpf_find_by_viewmodel`, `wpf_trace_command`,
`wpf_resolve_binding`, `wpf_inspect_viewmodel`, `wpf_trace_style`,
`wpf_trace_template`, `wpf_coverage_commands` — 7 semantic tools; total
Query subsystem 14.

Actual total: **15 (sync 9 + semantic 6; with `wpf_coverage_commands`)**.
Recounted with explicit audit below in §13.11.

### 13.4 Capture (4 tools)

`wpf_capture_region`, `wpf_visual_diff`, `wpf_visual_diff_update`,
`wpf_visual_diff_prune`.

### 13.5 Recording (8 tools)

`wpf_record_start`, `wpf_record_stop`, `wpf_recording_snapshot`,
`wpf_replay`, `wpf_replay_step`, `wpf_replay_rewind`, `wpf_induce_state_machine`,
`wpf_fetch_blob`.

### 13.6 Observability (4 tools)

`wpf_emit_trace`, `wpf_export_trace`, `wpf_score_accessibility`,
`wpf_query_etw`.

### 13.7 Multi-agent (4 tools)

`wpf_multi_agent_request_driver`, `wpf_multi_agent_yield`,
`wpf_multi_agent_barge`, `wpf_multi_agent_annotate`.

### 13.8 Live edit (4 tools)

`wpf_live_edit_style`, `wpf_live_edit_template`, `wpf_live_edit_xaml`,
`wpf_live_edit_revert`.

### 13.9 Federation (2 tools)

`wpf_federation_list_peers`, `wpf_federation_route_to`.

### 13.10 Headless / source (1 tool)

`wpf_source_ensure_hidden` — force-create the message-only `HwndSource`
if not yet created. Diagnostic aid.

### 13.11 Full recount

| Family | Tools |
|--------|-------|
| v3 retained | 15 |
| Input | 15 |
| Sync | 9 |
| Semantic | 6 |
| Capture | 4 |
| Recording | 8 |
| Observability | 4 |
| Multi-agent | 4 |
| Live edit | 4 |
| Federation | 2 |
| Headless | 1 |
| **Total** | **72** |

Doubled v4's count. Yes, 72 is a lot — the tool descriptions will be
terse and discoverability is optimized in §15.3.

### 13.12 Error taxonomy (full)

All v3 error codes retained, plus:

- `FidelityNotAvailable` — cascade found no strategy.
- `FidelityCapExceeded` — intent > session `MaxFidelity`.
- `CommandDisabled` — L0 selected, `CanExecute = false`.
- `NoInputSource` — no `PresentationSource`.
- `InternalApiMissing` — an `UnsafeAccessor` failed.
- `IncompatibleRuntime` — load-context or assembly-version mismatch.
- `PredicateInvalid` — parse error.
- `PredicateTimeout` — evaluation quota exceeded.
- `PredicateValueTooLarge` — resolved value > 64 KB.
- `PredicateVersionUnsupported` — unknown `$version`.
- `AutomationDisabled`, `MutationDisabled`, `LiveEditDisabled`,
  `MultiAgentDenied`.
- `HardwareInputRequiresReason`.
- `SubscriptionLimit`, `SubscriptionExpired`, `SubscriptionNotFound`.
- `VirtualClockNotInstalled`.
- `CaptureOutOfBounds`.
- `RecordingSignatureInvalid`, `RecordingProcessMismatch`.
- `PropertyRedacted` — predicate or query hit redacted property.
- `DispatcherBusy`, `DispatcherDead`.
- `CrossDispatcherLinearizationFailed`.
- `DriverLockHeld`, `DriverLockExpired`, `DriverLockDenied`.
- `FederationUnreachable`.
- `XamlSnippetRejected` (live edit sandbox).

Every error has a `suggestion` field. For input-tier errors the suggestion
is a machine-executable tool-call template:

```json
{
  "error": { "code": "CommandDisabled", "message": "...",
             "suggestion": { "tool": "wpf_click",
                             "args": { "nodeId": "0:42",
                                       "advanced": {"tier": "peer"}}}}
}
```

---

## 14. Performance Targets (hardened)

| Operation | v3 baseline | v4 target | **v5 target** |
|-----------|-------------|-----------|---------------|
| `wpf_get_session_info` | 2 ms | 2 ms | **< 500 µs** |
| `wpf_find_elements` (100 nodes) | 5 ms | 0.3 ms | **< 100 µs** |
| `wpf_get_visual_tree` (depth 5) | 15 ms | 1 ms | **< 500 µs** |
| `wpf_click` (L1) | — | 1 ms | **< 500 µs** |
| `wpf_type_text` (L3, 20 chars) | — | 3 ms | **< 1.5 ms** |
| `wpf_wait_for_property` hit | — | < 1 ms | **< 200 µs** |
| Subscription event e2e | — | < 5 ms | **< 2 ms** |
| `wpf_capture_region` (1 MP, WPF) | 50 ms | 5 ms | **< 3 ms** |
| `wpf_capture_region` (1 MP, D3D hook) | 50 ms | 10 ms | **< 5 ms** (shared-memory) |
| `wpf_visual_diff` (SSIM 1 MP) | — | 30 ms | **< 20 ms** |
| `wpf_visual_diff` (MobileCLIP, GPU) | — | — | **< 15 ms** |
| Tree version bump (1,000 watched) | — | — | **< 1 µs** |
| Agent cold start to first answer | — | — | **< 100 ms** |

Budget: every in-process tool completes in under 1/2 of a 60 Hz frame
(8 ms). Input + query + capture can pipeline within a single frame.

---

## 15. Tool Schemas (excerpts of the new / changed ones)

### 15.1 `wpf_click`

```
Input:
  nodeId: string
  advanced?: {
    tier?: "peer" | "event" | "input" | "hardware"
    autoFallback?: boolean                 default false
    reason?: string                        required if tier == "hardware"
    button?: "left" | "right" | "middle"  default "left"
    modifiers?: string[]                   e.g. ["Ctrl", "Shift"]
    dispatcherId?: number
  }
Output:
  chosenTier: string
  treeVersionBefore: long
  treeVersionAfter: long
  elapsedMs: double
  cacheHit: boolean
```

`fidelity` is `advanced.tier`; renamed to avoid the cognitive load v4 had
on every call.

### 15.2 `wpf_execute_command` (new — explicit L0)

```
Input:
  nodeId: string
  commandPropertyName?: string        default "Command"
  parameter?: any
  requireCanExecute?: boolean         default true
Output:
  commandFound: boolean
  canExecute: boolean
  executed: boolean
  treeVersionBefore: long
  treeVersionAfter: long
```

Explicit name: caller *knows* this is a semantic shortcut. No masquerade.

### 15.3 `wpf_wait_for_property` (flat, agent-friendly)

```
Input:
  nodeId: string
  propertyName: string
  expectedValue: any
  comparator?: "eq" | "ne" | "matches"  default "eq"
  timeoutMs?: number                    default 5000
Output:
  matched: boolean
  actualValue: any
  treeVersion: long
  timedOut: boolean
```

### 15.4 `wpf_subscribe` + notification

Unchanged from v4 except `$version: 1` required in predicate, and
three-mode delivery (§8.6).

### 15.5 Discovery assistance

Every tool's JSON-Schema `description` follows this template:

```
{
  "description": "Click an element via AutomationPeer.IInvokeProvider (L1).
                  Prefer this over wpf_execute_command when you want the
                  full automation-peer code path (visual feedback, peer
                  contract). Use wpf_execute_command if you want to skip
                  straight to the bound ICommand.",
  "properties": { ... }
}
```

Decision-tree hints written directly into descriptions. UX agent finding.

### 15.6 Error-response machine-readability

Error `suggestion` fields are structured `{ tool, args }` objects, not
prose, so agents can execute them directly without parsing.

---

## 16. User Stories and Migration Path

### 16.1 User stories

Prefix `US-AUTO-`. 120+ stories across 11 milestones. Groups:

- M0 Spikes: US-AUTO-001..005 (5)
- M1 Foundation: 010..030 (21)
- M2 L0/L1/L2: 040..060 (21)
- M3 Deterministic sync: 070..090 (21)
- M4 L3/L4: 100..115 (16)
- M5 Semantic: 120..135 (16)
- M6 Capture: 140..150 (11)
- M7 Recording: 160..180 (21)
- M8 Observability: 190..200 (11)
- M9 Multi-agent: 210..220 (11)
- M10 Live edit: 230..240 (11)
- M11 Release: 250..260 (11)

Total: ~176 user stories. Will expand into `BEADS.md` during planning.

### 16.2 Migration path (first-class)

`SnoopWPF.Agent.Shim.FlaUI` provides binary-compatible adapter types for
the FlaUI surface areas the consumer uses (MotionCatalyst: `AppSession`,
`FlaUIActionCatalog`, `AutomationElement` subset). Internally, each
adapter call maps to one or more MCP tool calls.

Dual-stack runtime: at test runtime, scenarios can opt per-step whether
to route through `shim.FlaUI` (adapter → MCP) or legacy FlaUI. CI gate
asserts behavioural equivalence: run the same scenario against both and
diff the final tree state.

`docs/migration-flaui-to-snoop.md` — per-FlaUI-API translation table.

Milestone M-MIGRATE is a sibling of M1–M11, runs in parallel with M2–M5
work, and completes when 90% of MotionCatalyst's UI test steps are
shim-routable.

---

## 17. Milestones (summary table)

| M# | Name | Key outputs |
|----|------|-------------|
| M0 | Spikes | 5 closed spikes; feasibility confirmed for all internal-API + tree-change + clock paths |
| M1 | Foundation | Co-located agent, `Console.Out` takeover, `WpfInternals`, `HeadlessSource` |
| M2 | Input L0/L1/L2 | 4 L0 tools + 4 L1 tools + L2 opt-in; profile-guided cache |
| M3 | Deterministic sync | Tree versioning multi-source; 3-mode delivery; 7 sync tools; virtual clock tier A+B+C |
| M4 | Input L3/L4 | 7 L3 tools; L4 audit; IME / composition input |
| M5 | Semantic | 6 semantic tools; redaction on `propertyPath`; discovery aids |
| M6 | Capture | ISnoopVisualCapture; ΔE/SSIM/MobileCLIP; out-of-band blob channel |
| M7 | Recording | HMAC-signed; time-travel; ring-buffer; state-machine induction |
| M8 | Observability | OTEL / ETW / Chrome trace; accessibility scoring |
| M9 | Multi-agent | Driver arbitration; observer sessions; federation protocol |
| M10 | Live edit | XAML injection; style/template hot-replace |
| M11 | Release | Full NuGet matrix; docs; CI green; accessibility baseline |
| M-MIGRATE | Parallel | FlaUI shim; dual-stack runtime; CI equivalence gate |

Parallelism: M3 can overlap M2; M5 can overlap M4; M6/M7/M8 can run
concurrently after M4; M9/M10 are late-stage and independent.

---

## 18. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Internal WPF API drift across runtimes | High | High (L3/L4 off) | Matrix CI against .NET 6/8/9; startup self-test; graceful session-level downgrade |
| Virtual clock IL rewrite fails on obfuscated assemblies | High | Medium (feature off for those apps) | Document tier A as primary; tier C opt-in only |
| `ValueChangedEventManager` weak-event subclass complexity | Medium | Medium (perf overhead) | Benchmark against 1,000 watched nodes; fallback to polling tier if > budget |
| `RenderTargetBitmap` fails in Windows Service context | Low | Medium (capture tests skip) | Detect interactive session at startup; disable capture in service contexts |
| D3D capture handler orientation/padding bugs in consumer code | High | Medium (wrong pixels) | Packed-BGRA contract in docs + integration test with known pattern |
| Session fan-out introduces driver-lock deadlock | Medium | High (agent hang) | Timeout on driver-lock acquisition; barge rights for Arbiter |
| Predicate DSL DoS via deeply nested expressions | Low | Medium (CPU) | Depth + node cap; fuzz CI |
| Recording HMAC key leak via transport dump | Low | High (replay forgery) | Key rotated per session; never logged; key-derivation from session token |
| Live edit XAML snippet escapes sandbox | Low | High (RCE) | Type-allowlist; analyzer-based reject; disabled by default |
| MobileCLIP model size (200 MB) bloats NuGet | Medium | Low | Separate `SnoopWPF.Agent.Automation.Skia` package; lazy download on first use |
| Federation protocol opens cross-app surface | Medium | Medium | Loopback only; auth-token per peer; owner-user-only |
| State-machine induction produces misleading models | High | Low (informational) | Abstraction policy configurable; documented as advisory |
| Stdout takeover breaks third-party lib logging | Medium | Medium | Module-load prohibition list; documented consumer constraint |
| OTEL export floods shared trace backend | Low | Low | Local-first default (file sink); endpoint opt-in |
| Multi-dispatcher linearization stalls one dispatcher | Low | Medium | Per-dispatcher queue with fairness; priority inversion detection |
| Accessibility score false positives destabilize scores | Medium | Low | Score ranges published with confidence intervals |
| Recording ring-buffer memory cost | Medium | Medium | Default size small; explicit opt-in to larger buffer; overflow eviction |
| Profile-guided cache staleness after app refactor | Medium | Low | Cache invalidates on element-type change; explicit reset tool |
| Injection mode's L1 capability boundary surprises users | Medium | Low | Session-info tool exposes `MaxFidelity` explicitly |

---

## 19. Open Questions

1. **Federation trust model** (Appendix C) — currently "loopback + per-peer
   token." Is there a case for something stronger, like mTLS even on
   loopback?
2. **MobileCLIP inference location** — on-device CPU vs. DirectML GPU vs.
   offloaded. On-device is simpler, GPU is faster. Ship both?
3. **IL rewrite opt-in mechanism** — `[assembly: SnoopVirtualClockAllowed]`
   per assembly, or process-global flag? Per-assembly safer.
4. **Recording format versioning** — stable on-disk schema from v5.0, or
   breaking changes allowed in minor releases?
5. **Live edit scope persistence** — should live edits survive app restart
   when `persistLiveEdits: true`, or always revert?
6. **State-machine induction abstraction policy** — ship built-in policies
   (by-visibility, by-text, by-DP-set), or pure byo?
7. **Cross-process replay safety** — is `executableHash` sufficient, or do
   we need code-signing validation too?
8. **Multi-agent barge audit** — log to stderr (insufficient per H3) or
   audit log? Audit log, but worth double-check.
9. **Third-party injection surface** — same tool surface as co-located, or
   a reduced set that doesn't expose (e.g.) live edit?
10. **`ITimeProvider` adoption in consumer repos** — is it reasonable to
    make `TimeProvider` adoption a hard prerequisite for the virtual
    clock in MotionCatalyst, or do we have to ship tier C (IL rewrite)
    from day one?

---

## 20. Appendix

### A. WPF internals reference (corrected)

All via `WpfInternals` in `SnoopWPF.Agent.Input.Probabilistic`.

```csharp
internal static class WpfInternals
{
    // L3 mouse
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern RawMouseInputReport ctor_RawMouseInputReport(
        InputMode mode, int timestamp, PresentationSource inputSource,
        RawMouseActions actions, int x, int y, int wheel,
        IntPtr extraInformation);

    // L3 keyboard
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern RawKeyboardInputReport ctor_RawKeyboardInputReport(
        PresentationSource inputSource, InputMode mode, int timestamp,
        RawKeyboardActions actions, int scanCode, bool isExtendedKey,
        bool isSystemKey, int virtualKey, IntPtr extraInformation);

    // SINGLE overload accessor (v4 had two conflicting ones).
    // Call site casts the concrete report to InputReport base.
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern InputReportEventArgs ctor_InputReportEventArgs(
        InputDevice inputDevice, InputReport report);

    // Raw text-composition
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern RawTextInputReport ctor_RawTextInputReport(
        PresentationSource inputSource, InputMode mode, int timestamp,
        bool isDeadCharacter, bool isSystemKey, bool isControl,
        char characterCode);

    // HwndSource from HWND
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod,
                    Name = "CriticalFromHwnd")]
    public static extern HwndSource CriticalFromHwnd(IntPtr hwnd);

    // InputManager canonical current
    // (public; included for symmetry)
    public static InputManager CurrentInputManager => InputManager.Current;
}
```

Every accessor fails visibly at startup if the target is missing; session
`MaxFidelity` is capped accordingly.

### B. Threat model

**Asset**: running WPF application process; user data within it;
developer secrets in recordings and baselines.

**Threat actors**:
- Local unprivileged user (shared machine).
- Malicious MCP client (compromised Claude session, prompt injection).
- Malicious federated peer.
- Malicious consumer library loaded into the same process.

**Trust boundaries**:
- User session ↔ other local users: `%LOCALAPPDATA%` owner-only ACLs;
  recording HMAC; audit log HMAC.
- Agent ↔ MCP client: session token; bearer on HTTP; PID + start-time
  verification.
- Agent ↔ federated peer: per-peer token; loopback-only transport.
- Agent ↔ target process: same-process trust; `Console.Out` takeover.

**Mitigations**: enumerated in §11 per finding. Residual risks: third-party
libraries in the target process can read agent-process memory (including
recording HMAC keys); this is an accepted limitation of in-process design
and applies equally to any debugger/inspector. Document explicitly.

### C. Multi-agent federation protocol

Out-of-band from MCP. Loopback TCP or Unix-domain sockets; TLS optional.

Peer discovery: every agent writes a discovery file to
`%LOCALAPPDATA%\SnoopWPF\peers\{pid}.json` with endpoint + per-peer token.
`wpf_federation_list_peers` enumerates live peer files (stale entries
pruned after 30 s without heartbeat).

Peer session: federation client opens a second MCP-like connection with
`Authorization: Bearer {peerToken}`. Peer agent treats the connection as
an external session, same policy model.

Routing: `wpf_federation_route_to(peerId, toolName, args)` forwards a tool
call to the peer; response marshalled back. Tool calls can cascade (A → B
→ C) but depth capped at 3 to prevent loops.

### D. State-machine induction abstraction policies

Built-in:
- `VisibilityOnly` — equivalence by `{ nodeId, Visibility }`.
- `Interactive` — equivalence by `{ nodeId, IsEnabled, IsHitTestVisible,
  Visibility }`.
- `Displayed` — equivalence by `{ nodeId, Text, IsChecked, IsSelected }`.
- `Full` — equivalence by every watched DP.

Custom policies: user provides a predicate object that returns a canonical
state-key given a tree snapshot.

### E. Performance methodology

Benchmark harness: `SnoopWPF.Agent.PerformanceTests` runs under BenchmarkDotNet
on Windows Server 2022 + Windows 11, net6/net8/net9, with both cold-cache
and warm-cache scenarios. Results published per release to
`docs/perf/v{version}.md` with flame graphs.

Regression gate: CI fails if any listed target (§14) regresses > 10% p99.

### F. Glossary

- **Co-located mode** — target WPF app hosts the MCP server in-process.
- **Driver** (multi-agent) — the sole session allowed input mutation.
- **Fidelity tier** — L0..L4 input simulation depth.
- **Profile-guided cache** — fidelity-selection cache keyed on element
  signature.
- **Ring buffer recording** — passive continuous capture; last N events
  retained.
- **Session policy** — mode, fidelity cap, flags; bound at session creation.
- **State-machine induction** — Mealy machine derived from recorded UI
  sessions.
- **Temporal predicate** — predicate with window / transition / stability
  operators.
- **Tree version** — monotonic counter bumped on any watched tree mutation.
- **Virtual clock** — deterministic time provider, three cooperation tiers.
- **Weak-event manager** — WPF pattern for leak-free DP change subscriptions.

---

*End of PRD v5. The North Star. Next: two oracle passes to stress-test
before BEAD expansion.*
