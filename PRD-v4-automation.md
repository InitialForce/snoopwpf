# PRD v4: SnoopWPF.Agent Automation — High-Fidelity WPF UI Automation via MCP

> Supersedes the "Automated UI testing" non-goal in PRD v3. v3 (inspection) stands as
> the foundation; v4 extends the surface to input simulation, deterministic
> synchronization, semantic navigation, co-located MCP hosting, and high-fidelity
> capture. This document is additive — no v3 user story is reopened, no existing tool
> is deprecated. Every new tool is opt-in; the read-only inspection mode from v3
> remains the default.

---

## 0. Reading This Document

Sections 1–4 establish scope and quality gates. Section 5 is the architectural core —
read it before any downstream section. Sections 6–10 are subsystem designs that can
be read independently. Sections 11–13 cover security, perf, and the full tool
inventory. Sections 14–17 define user stories, milestones, risks. Section 18 collects
open questions. The appendix enumerates the WPF internal APIs we touch.

Conventions:

- New code: file paths use backslash for Windows, forward slash for POSIX where
  unavoidable. Project names keep the `SnoopWPF.Agent.*` prefix.
- User story IDs: `US-AUTO-001..` to avoid collisions with v3's `US-001..US-024`.
- Bead IDs: `BEAD-AUTO-001..`.
- Tool names: new MCP tools keep the `wpf_` prefix; input tools use `wpf_input_*`
  where there's a fidelity-level variant, otherwise a flat `wpf_click`/`wpf_type_text`
  naming.

---

## 1. Overview & Problem Statement

### 1.1 What v4 adds

v3 shipped a read-mostly WPF inspector: 15 MCP tools that walk the visual tree,
inspect properties, resolve bindings, dump resources, and capture screenshots. A
single opt-in mutation tool (`wpf_set_property`) covers surface-level property
writes. That surface is sufficient for debugging — insufficient for
AI-driven UI automation.

v4 closes the gap. It adds:

- **Input simulation** at five distinct fidelity levels (command, automation peer,
  routed event, input manager, hardware) so a caller picks semantic faithfulness
  per-call, instead of pinning the whole system to one backend.
- **Deterministic synchronization** via tree versioning, server-sent subscription,
  and a virtual clock — eliminating time-based polling entirely.
- **Semantic navigation** — ViewModel-aware addressing, command reverse-lookup,
  binding resolution — so automation speaks in domain terms rather than visual-tree
  shapes.
- **Co-located MCP hosting** — the target app is the MCP server. No middleman, no
  second process, no COM boundary.
- **High-fidelity capture** with a D3D hook for hardware-accelerated surfaces.
- **Session recording and deterministic replay** for flake diagnosis.

### 1.2 Why WPF specifically

WPF is the only mainstream UI framework where all five invocation levels are
accessible from in-process managed code. Win32/WinForms lacks the Dispatcher
abstraction; WinUI 3 obscures the input stack behind XamlRoot; browser UIs require
round-tripping a script host. WPF's managed input pipeline — `InputManager`,
`RawKeyboardInputReport`, `Keyboard`/`Mouse` input providers — lets us drive the
exact same code paths that Windows uses, at in-process latency, deterministically.

### 1.3 The problem we are actually solving

AI agents driving UI automation today pick between two bad options:

1. **UI Automation (UIA) cross-process** via FlaUI/Appium/WinAppDriver. Pays a
   1–10 ms COM marshaling cost per call, hangs arbitrarily on any unresponsive
   desktop window, is essentially non-functional in headless, and silently skips
   patterns the target control doesn't expose. Observed pain in the MotionCatalyst
   code base (see companion plan `wpf-mcp/docs/plans/snoop-integration-plan.md`):
   161 s screenshots, hard-timeout failures, fragile multi-instance handling.

2. **Pure in-process "test shims"** where tests call ViewModels directly. Fast and
   deterministic, but tests only the business logic — misses visual triggers,
   template behaviour, input bindings, focus chains, style interactions.

v4 gives callers both at once: in-process latency with tier-selectable fidelity.

### 1.4 Target users

- Primary: **Claude Code / Claude Desktop** driving QA of owned WPF applications.
- Primary: **MotionCatalyst** (the reference consumer; see `docs/plans/` in the
  consumer repo).
- Secondary: human developers writing automation scripts via `snoop-cli` or direct
  MCP tools.
- Secondary: third-party WPF apps that can't modify source (injection mode, carried
  forward from v3).

---

## 2. Goals

### 2.1 Functional

- Expose input simulation as MCP tools with explicit fidelity control.
- Provide deterministic "wait-until" semantics with zero polling.
- Offer ViewModel-centric navigation (`find_by_viewmodel`, `trace_command`,
  `resolve_binding`) in addition to visual-tree navigation.
- Support co-located mode where the target WPF app is itself the MCP server.
- Capture visual output for elements using GPU-accelerated surfaces.
- Record and replay input sessions deterministically.

### 2.2 Non-functional

- **Latency**: p99 < 5 ms for any read tool, < 10 ms for any non-hardware input
  tool. Hardware-level input (`fidelity: "hardware"`) allowed up to 20 ms including
  focus change.
- **Determinism**: same tool sequence on same app state produces identical results.
  No wall-clock timing in any tool's return condition.
- **Headless parity**: every tool works identically when MotionCatalyst is run with
  `--headless` (no visible window). Hardware-fidelity input is the only tier that
  may degrade or refuse in headless.
- **Upstream merge-ability**: v4 additions sit in new projects (`SnoopWPF.Agent.*`)
  so rebase onto `snoopwpf/snoopwpf` remains mechanical.

### 2.3 Scope reversal from v3

v3 Non-Goals line 37 said: *"Automated UI testing — this is an inspection/debugging
tool, not a test runner."* v4 **reverses this** for owned apps in co-located mode.
Rationale: co-location eliminates the class of reliability issues that made v3
cautious (cross-process pipe lifecycle, injection-window races, process crashes
without session teardown). When the agent lives in the target app, UI automation
becomes a first-class use case with no new failure modes.

---

## 3. Non-Goals

These remain out of scope for v4:

- **Cross-platform** — still WPF/Windows only. `LangVersion` and dependency
  assumptions do not relax.
- **Non-WPF UI frameworks** — no WinForms, MAUI, Avalonia, UWP, WinUI.
- **Remote debugging** — automation surface still binds to `127.0.0.1` only.
- **Record-and-replay as a compliance artifact** — recording is a debugging and
  flake-diagnosis aid; it is not designed as a regulated audit log.
- **Production use** — all tools ship with an `EnableAutomation: false` default.
  Consumers must explicitly opt in, same as v3's `EnableMutation`.
- **BDD / scenario runners** — we expose MCP tools; we do not build a SpecFlow-
  style runner on top of them. Consumers (including MotionCatalyst's existing
  SpecFlow suite) keep their own runners and call us as a library.
- **Replacing FlaUI in the consumer repo** — v4 ships the primitives; the consumer
  gets to migrate at whatever pace suits them.
- **Injection mode for automation tier L3/L4** — tiers L0–L2 work over injection
  mode. Tiers L3/L4 (`InputManager`, `SendInput`) are **co-located-only** because
  they require direct access to the target process's message pump. Injection mode
  gets reduced fidelity; this is documented, not a bug.
- **Test recording via the GUI** — the Snoop GUI retains read-only status. No
  record-button UI; recording is an MCP call.

---

## 4. Quality Gates (per milestone)

Each milestone inherits all prior gates.

**M1 (Co-location foundation)**
- `dotnet build Snoop.sln` passes.
- New project `SnoopWPF.Agent.Input` compiles with its internal-API shims.
- `SnoopWPF.Agent.Tests` adds `CoLocatedHostingTests` — `SnoopAgent.Start(mode: Stdio)`
  round-trips `wpf_get_session_info` without a separate host process.
- Regression: all v3 integration tests still green.

**M2 (L0/L1 input)**
- `wpf_click`, `wpf_type_text` with `fidelity: "command" | "peer"` implemented
  against a reference app exercising both bound-command and UIA-peer code paths.
- Sample app `SnoopWPF.Agent.SampleApp` gains 3 new scenarios (button with
  RelayCommand, TextBox two-way binding, CheckBox with Toggle peer).
- Integration test asserts ViewModel state after each input tool call.

**M3 (Deterministic sync)**
- Tree versioning wired into `NodeRegistry`; `wpf_wait_until` and `wpf_subscribe`
  implemented end-to-end.
- Zero calls to `Thread.Sleep` / `Task.Delay` in any tool's success path.
- Virtual clock available as opt-in (`SnoopAgentOptions.InstallVirtualClock = true`).

**M4 (L2/L3 input + semantic navigation)**
- `RaiseEvent` and `InputManager` fidelity levels implemented.
- `wpf_find_by_viewmodel`, `wpf_trace_command`, `wpf_resolve_binding` implemented.
- Integration test: focus-chain test using L3 fidelity (key-binding invocation
  verified via `InputGesture` on a `RoutedCommand`).

**M5 (High-fidelity capture + recording)**
- `ISnoopVisualCapture` extensibility point defined; reference D3D implementation
  in the sample app.
- `wpf_visual_diff` with perceptual diff scoring.
- `wpf_record_start` / `wpf_record_stop` / `wpf_replay` end-to-end, with a
  deterministic replay integration test.

**M6 (Release)**
- NuGet package `SnoopWPF.Agent.Automation` published (separate from `SnoopWPF.Agent`
  to keep v3 consumers unchanged).
- CI green on Windows Server 2022 + Windows 11.
- README updated with tier-selection decision tree.

---

## 5. Architecture

### 5.1 Three integration modes

v3 defined two modes (NuGet, injection). v4 introduces a third — **co-located MCP
server** — and promotes it to primary for owned applications.

| Mode | Who hosts the MCP server | Transport | Input fidelity ceiling | Use when |
|------|--------------------------|-----------|------------------------|----------|
| **Co-located** | The target WPF app itself | stdio (primary), named pipe (secondary) | **L4** (SendInput) | You own the app. Best perf, determinism, fidelity. |
| **NuGet (v3)** | Target app, separate MCP process talks via pipe | stdio + named pipe | L2 (RaiseEvent) | You own the app but can't change the entry point. |
| **Injection (v3)** | External `snoop-mcp.exe` | stdio external, pipe to target | L1 (AutomationPeer) | Third-party / closed-source apps. |

Fidelity ceiling comes from where the tool runs:

- **L0 Command / L1 AutomationPeer**: Run inside the target's Dispatcher, same as
  every v3 read tool. All three modes support these.
- **L2 RaiseEvent**: Synthesizes WPF routed events. Same constraints as L0/L1
  but requires crafting `InputEventArgs` with a valid `InputSource`, which in
  injection mode requires extra care (see 6.3).
- **L3 InputManager**: Requires constructing `RawInputReport` subclasses and calling
  `InputManager.Current.ProcessInput`. The `InputReport` constructor is `internal`;
  we use `UnsafeAccessor` to reach it. Works in all modes but injection mode adds
  risk of version-mismatched internal types (target app's `.NET` runtime vs.
  agent's targeting).
- **L4 SendInput**: Win32 hardware input injection. Steals focus. Co-located only
  — injection mode can't reliably control the foreground window state.

### 5.2 Co-located mode

The target app boots the MCP server on its own Dispatcher thread when launched
with `--mcp-stdio`. Concrete flow:

```
MotionCatalyst.exe --mcp-stdio --headless
  │
  ├─ App.OnStartup():
  │   if (args.Contains("--mcp-stdio"))
  │       SnoopAgent.StartCoLocated(this, new SnoopAgentOptions {
  │           Transport = TransportMode.Stdio,
  │           EnableAutomation = true,
  │           EnableMutation = true
  │       });
  │
  ├─ SnoopAgent redirects stdout as MCP transport (stderr stays for logs).
  ├─ MCP server runs on a dedicated transport thread.
  ├─ Every tool marshals to the app's Dispatcher via InvokeAsync.
  └─ Process exit = MCP server exit. No lifecycle coupling to a host.
```

`.mcp.json` in the consumer repo:

```json
{
  "mcpServers": {
    "motioncatalyst": {
      "command": "MotionCatalyst.exe",
      "args": ["--mcp-stdio", "--headless"]
    }
  }
}
```

No `dotnet run`, no intermediate process, no UIA, no FlaUI. Transport round-trip
is effectively OS pipe latency (~50 µs) plus a Dispatcher marshal (~100 µs).

### 5.3 Tiered invocation stack

The central design decision: **every mutation tool takes a `fidelity` parameter**
and dispatches to one of five implementations. The caller names the semantic level
they want. Auto-select (default) picks the highest level that is (a) guaranteed to
work for the target element and (b) deterministic.

```
L0  Command       - Resolve ICommand binding, CanExecute gate, Execute().
                    Testing: business logic path only. No visual feedback.
                    Cost: < 1 ms. Success: deterministic.

L1  AutomationPeer - UIElementAutomationPeer.CreatePeerForElement(e)
                     .GetPattern(PatternInterface.Invoke).Invoke()
                    Testing: UIA pattern contract. Same handlers as real UIA
                    without COM.
                    Cost: ~ 1 ms. Success: deterministic if peer exists.

L2  RaiseEvent    - Construct InputEventArgs, element.RaiseEvent(e).
                    Testing: routed events, triggers, EventSetters,
                    tunnel/bubble, attached events.
                    Cost: ~ 1 ms. Success: deterministic.

L3  InputManager  - Construct RawInputReport, InputManager.Current
                    .ProcessInput(args).
                    Testing: key bindings, focus chain, preview input,
                    IME composition, InputBindings.
                    Cost: ~ 2 ms. Success: deterministic.

L4  SendInput     - Win32 SendInput to foreground window.
                    Testing: OS-level input path, cross-window drag, UAC.
                    Cost: 5–20 ms + focus race. Success: probabilistic
                    if foreground window changes under us.
```

Auto-select rules (applied in order):

```
click_element(target, fidelity: "auto"):
  1. if target has a Command binding and Command != null    → L0
  2. else if target's AutomationPeer provides IInvokeProvider → L1
  3. else if target is a visible UIElement with hit-testable geometry → L3
  4. else if target is in visual tree but not hit-testable → L2 (RaiseEvent)
  5. else                                                   → error

type_text(target, text, fidelity: "auto"):
  1. if target is TextBox/PasswordBox/RichTextBox with TwoWay binding → L0 (SetValue)
  2. else if target has IValueProvider → L1
  3. else if target has Keyboard.Focus → L3 (per-char RawKeyboardInputReport)
  4. else → L4 (SendInput after focus)
```

Callers can override with `fidelity: "command" | "peer" | "event" | "input" | "hardware"`.

### 5.4 Deterministic synchronization

Three primitives, no polling anywhere.

1. **Tree version** — a `long VersionCounter` in `NodeRegistry`, incremented
   whenever any monitored tree mutation occurs (child add/remove, DP change on a
   watched property). Every tool response includes `treeVersion` in its envelope.

2. **Wait-until** — `wpf_wait_until(predicate, minVersion?, timeoutMs?)` blocks on
   a server-side `TaskCompletionSource` that completes when the next tree change
   with version > `minVersion` makes `predicate` true. Predicate is a restricted
   JSON-expression DSL (see 7.2), not arbitrary code.

3. **Subscriptions** — `wpf_subscribe(predicate)` returns a subscription ID and
   opens a server-sent-event (SSE) channel on the MCP connection. The server
   fires an event each time the tree changes and the predicate transitions to
   true. Claude connects once, reacts many times. Eliminates the N×polling cost
   entirely for long-lived scenarios.

A **virtual clock** (opt-in per session) replaces `DispatcherTimer`, `Stopwatch`,
and `DateTime.UtcNow` usage across the session via an `ITimeProvider`
abstraction already present in recent .NET. For code paths that bypass the
abstraction (direct `System.Threading.Timer` etc.), we document the limitation
and recommend consumer cooperation.

### 5.5 Project structure (delta from v3)

```
Snoop.sln
│
│ ... (all v3 projects retained) ...
│
│── SnoopWPF.Agent.Input/                NEW — tiered input simulation
│     TargetFrameworks: net6.0-windows;net8.0-windows  (UseWpf=true)
│     Contents:
│       - IInputStrategy interface, five concrete strategies
│         (CommandStrategy, PeerStrategy, EventStrategy,
│          InputManagerStrategy, SendInputStrategy)
│       - InputStrategySelector (auto-select logic)
│       - Internal-API shims via UnsafeAccessor (RawInputReport, etc.)
│     Dependencies: SnoopWPF.Agent.Contracts, Snoop.Core (for visual helpers),
│                   PresentationCore, PresentationFramework, WindowsBase
│     Output: Class library
│
│── SnoopWPF.Agent.Sync/                 NEW — deterministic sync primitives
│     TargetFrameworks: net6.0-windows;net8.0-windows
│     Contents:
│       - TreeVersionTracker (subscribes to VisualTreeHelper mutation hooks)
│       - SubscriptionManager (SSE over MCP)
│       - WaitUntilBroker
│       - VirtualClock + ITimeProvider shim installer
│       - PredicateExpressionEvaluator (JSON-DSL)
│     Dependencies: SnoopWPF.Agent.Contracts, Snoop.Core
│     Output: Class library
│
│── SnoopWPF.Agent.Semantic/             NEW — ViewModel-aware navigation
│     TargetFrameworks: net6.0-windows;net8.0-windows
│     Contents:
│       - DataContextWalker (finds nodes by DataContext type + predicate)
│       - CommandReverseIndex (ICommand → bound elements)
│       - BindingResolver (rich binding diagnostic)
│     Dependencies: SnoopWPF.Agent.Contracts, Snoop.Core
│     Output: Class library
│
│── SnoopWPF.Agent.Capture/              NEW — high-fidelity visual capture
│     TargetFrameworks: net6.0-windows;net8.0-windows
│     Contents:
│       - ISnoopVisualCapture (extensibility hook for D3D/OpenGL surfaces)
│       - CompositeRenderStrategy (WPF RenderTargetBitmap + D3D hook)
│       - BaselineStore (PNG on disk, optionally inline blobs)
│       - PerceptualDiff (SkiaSharp-based ΔE / SSIM)
│     Dependencies: SnoopWPF.Agent.Contracts, SkiaSharp (optional),
│                   Snoop.Core
│     Output: Class library
│
│── SnoopWPF.Agent.Recording/            NEW — session record/replay
│     TargetFrameworks: net6.0-windows;net8.0-windows
│     Contents:
│       - SessionRecorder (subscribes to input strategy events + tree version
│         changes, writes framed JSON)
│       - SessionReplayer (reads framed JSON, re-invokes strategies)
│       - SessionBlob (DTO)
│     Dependencies: SnoopWPF.Agent.Contracts, SnoopWPF.Agent.Input,
│                   SnoopWPF.Agent.Sync
│     Output: Class library
│
│── SnoopWPF.Agent.Automation/           NEW — tool package + MCP wiring
│     TargetFramework: net8.0-windows
│     Contents:
│       - InputTools, SyncTools, SemanticTools, CaptureTools, RecordingTools
│         ([McpServerToolType] classes)
│       - SnoopAutomationOptions (nested in SnoopAgentOptions)
│       - StartCoLocated() entry point
│     Dependencies: all the above + ModelContextProtocol
│     Output: Class library + NuGet package
│
│── SnoopWPF.Agent.Automation.Tests/     NEW — unit + integration tests
│── SnoopWPF.Agent.SampleApp/            EXTENDED — scenarios for each subsystem
```

### 5.6 Dependency graph (delta)

```
SnoopWPF.Agent.Automation (NuGet, net8.0-windows)
  └─> SnoopWPF.Agent.Input
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> Snoop.Core
  └─> SnoopWPF.Agent.Sync
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> Snoop.Core
  └─> SnoopWPF.Agent.Semantic
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> Snoop.Core
  └─> SnoopWPF.Agent.Capture
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> Snoop.Core
  │     └─> SkiaSharp (optional, soft reference)
  └─> SnoopWPF.Agent.Recording
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> SnoopWPF.Agent.Input
  │     └─> SnoopWPF.Agent.Sync
  └─> SnoopWPF.Agent.Engine (from v3)
  └─> SnoopWPF.Agent.Tools (from v3)
  └─> ModelContextProtocol
```

### 5.7 Transport

Same options as v3 (stdio primary, named pipe secondary), with one addition:

**SSE-over-MCP for subscriptions.** MCP supports `notifications/*` messages for
server → client push. `wpf_subscribe` returns a subscription ID and the server
then emits `notifications/wpf_subscription` messages as changes occur. No new
transport; we piggyback on existing MCP notification semantics.

Injection mode retains the v3 pipe protocol for RPC; subscription events tunnel
through the same pipe as `PipeNotify` frames alongside the existing
`PipeRequest` / `PipeResponse` / `PipeCancel`.

### 5.8 Threading model

Unchanged from v3: every tool method marshals to the target Dispatcher via
`Dispatcher.InvokeAsync(DispatcherPriority.Send)`. Two additions:

1. **Input strategies marshal differently.** L0/L1/L2 run synchronously inside the
   InvokeAsync block. L3 queues a `ProcessInput` call with
   `DispatcherPriority.Input` (the priority the real Windows message pump uses).
   L4 runs on the transport thread and calls `SendInput` directly, then awaits a
   `Dispatcher.InvokeAsync(..., DispatcherPriority.ApplicationIdle)` to let the
   real input be processed before returning.

2. **Subscription event dispatch.** Tree mutation events fire on the Dispatcher.
   The `SubscriptionManager` debounces bursts (16 ms window = one WPF frame),
   evaluates predicates on the Dispatcher, then posts notifications to the
   transport thread for delivery.

---

## 6. Input Simulation Subsystem

### 6.1 Fidelity levels in depth

**L0 Command** — `IInputStrategy.Command`

- Applicable to any element where a `DependencyProperty` named `Command` exists
  and is bound to an `ICommand` (ButtonBase.Command, Hyperlink.Command,
  MenuItem.Command, InputBinding.Command, custom).
- Resolution: `DependencyPropertyDescriptor.FromName("Command", elementType,
  elementType).GetValue(element) as ICommand`.
- Invocation: `CanExecute(parameter)` → `Execute(parameter)`. `parameter` sourced
  from the element's `CommandParameter` property if present.
- What's NOT tested: visual state change (e.g. `IsPressed=true` during click),
  routed `Click` event handlers not on the VM, animations, style triggers keyed
  on `IsPressed`.

**L1 AutomationPeer** — `IInputStrategy.Peer`

- Calls `UIElementAutomationPeer.CreatePeerForElement(element)` or the control's
  explicit `OnCreateAutomationPeer` override. Resolves the requested
  `PatternInterface` (`Invoke`, `Toggle`, `Value`, `ExpandCollapse`, etc.).
- Identical code path to what FlaUI/UIA does, minus the COM boundary.
- If no peer or no pattern, returns `ElementDoesNotSupportFidelity` error. Caller
  can fall back with explicit lower fidelity.

**L2 RaiseEvent** — `IInputStrategy.Event`

- Synthesizes `MouseButtonEventArgs`, `KeyEventArgs`, `TextCompositionEventArgs`.
- Uses `PresentationSource.FromVisual(element)` to obtain the `InputSource`
  required by the `InputEventArgs` constructor. Injection mode: if visual is
  not parented to an `HwndSource`, returns `NoInputSource` error.
- For clicks: raises `PreviewMouseDown` → `MouseDown` → `PreviewMouseUp` →
  `MouseUp` with correct `Handled` flow. Manually raises `Click` for ButtonBase.

**L3 InputManager** — `IInputStrategy.InputManager`

- Constructs `RawMouseInputReport` / `RawKeyboardInputReport` via
  `UnsafeAccessor` into the internal constructors, then calls
  `InputManager.Current.ProcessInput(new InputReportEventArgs(...))`.
- Runs on `DispatcherPriority.Input` to match the real message pump priority.
- **This is the ceiling for "what a user would trigger" without leaving the
  process.** It drives the WPF input stage chain identically to real Windows
  input: PreProcessInput → PreNotifyInput → actual routed events → PostNotifyInput.
- Works headless because `HwndSource` is fully constructed even for hidden
  windows; `InputManager` doesn't care about window visibility.

**L4 SendInput** — `IInputStrategy.Hardware`

- Win32 `SendInput` via P/Invoke. Focus-stealing. Cross-window capable.
- Used for: testing global hotkeys; dragging between windows; UAC elevation
  prompts; anything where the test genuinely needs the OS to deliver the input.
- Co-located mode only. Injection mode returns `FidelityNotAvailable`.

### 6.2 Auto-select heuristic

The selector is a deterministic cascade. See 5.3 for the rules. One non-obvious
detail: if `fidelity: "auto"` picks L0 (Command) and `CanExecute` returns false,
the selector does **not** fall through to L1 — it returns `CommandDisabled`.
Rationale: `CanExecute` = false is often the correct test assertion; silently
escalating would mask real UI state.

Override via `fidelity: "auto-escalate"` to enable fall-through on L0 failure.

### 6.3 WPF internal API access

The subsystem reaches several `internal` types. We gate access through a single
`WpfInternals` class using `[UnsafeAccessor]` (net8+) or `MethodInfo.Invoke`
(net6+).

| Type / member | Why | .NET 8 mechanism |
|---|---|---|
| `System.Windows.Input.RawMouseInputReport..ctor` | L3 mouse input | `UnsafeAccessor(UnsafeAccessorKind.Constructor)` |
| `System.Windows.Input.RawKeyboardInputReport..ctor` | L3 keyboard input | Same |
| `System.Windows.Input.InputReportEventArgs..ctor` | L3 wrapper args | Same |
| `PresentationSource.AddSource` / `RootSourceProperty` | Verify `PresentationSource` on hidden windows | Public |
| `HwndSource.CriticalFromHwnd` | Map `HWND` → `HwndSource` | `UnsafeAccessor` into internal static |
| `KeyboardDevice.ModifierKeysFromSlimResult` | Apply modifier set to synthesized events | `UnsafeAccessor` |
| `InputManager.Current.PrimaryKeyboardDevice.Focus()` | Ensure focused element before L3 keys | Public |

All internal-API uses are wrapped in a `try/catch (MissingMethodException)` with
a graceful fallback and a **one-line warning logged to stderr** identifying the
exact missing accessor. If any accessor misses, the affected fidelity level is
disabled for the session (not the process) and `wpf_get_session_info` reports
`capabilities` minus the disabled level.

Version pinning: the Input subsystem multitargets `net6.0-windows;net8.0-windows`
and we run the accessor test suite against PresentationCore from both the
Microsoft.WindowsDesktop.App.Ref packs and against the live runtime in CI.
Breakage from a framework patch surfaces in CI, not in production.

### 6.4 Per-tool input strategy matrix

```
wpf_click        L0 → L1 → L3 → L2 (as last resort for focusless elements)
wpf_double_click L1 (Invoke twice) → L3 → L2
wpf_right_click  L1 (ExpandCollapse if menu) → L3 → L2
wpf_type_text    L0 (TextBox.Text SetValue) → L1 (IValueProvider) → L3 (per-char)
wpf_send_keys    L3 (explicit chord + modifier) → L4 (global hotkeys)
wpf_drag         L3 (MouseDown + Move sequence + MouseUp) → L4 (cross-window only)
wpf_hover        L3 (MouseMove only)
wpf_scroll       L0 (ScrollIntoView if list) → L1 (IScrollProvider) → L3 (wheel)
wpf_focus        Public Keyboard.Focus() — no tier needed
```

---

## 7. Deterministic Synchronization Subsystem

### 7.1 Tree versioning

A `long _version` in `NodeRegistry`, incremented on:

1. `VisualTreeHelper.GetChildrenCount` result changes for any registered node
   (detected via a weak subscription on `VisualTreeChanged` routed to all
   registrations).
2. `DependencyPropertyChanged` on a *watched property* of a registered node.
3. Explicit bump via `NodeRegistry.Bump()` for mutations the registry can't see
   directly (e.g., `ItemsControl.ItemsSource` rebind).

Watched properties default to the set that matters for automation:
`IsEnabled`, `Visibility`, `IsHitTestVisible`, `Text`, `IsChecked`, `IsSelected`,
`Value`, `Content`, `ItemsSource`. Consumers may extend via
`SnoopAutomationOptions.WatchedProperties`.

Cost: the hook is installed lazily per registered node via `PropertyDescriptor
.AddValueChanged`, with automatic teardown when the node's `WeakReference`
expires. Worst case ~9 handlers × N registered nodes. Mitigation: registration
happens on first query, not on tree construction — typical working set is a few
hundred nodes.

### 7.2 Predicate DSL

To avoid executing arbitrary code on the Dispatcher, predicates are JSON:

```json
{
  "$and": [
    { "property": "IsVisible", "op": "eq", "value": true },
    { "property": "Text", "op": "matches", "value": "^Recording$" },
    { "automationId": "StartButton" }
  ]
}
```

Operators: `eq`, `ne`, `gt`, `lt`, `ge`, `le`, `contains`, `matches` (regex,
capped at 1000 chars input), `in`. Compositors: `$and`, `$or`, `$not`. Target:
a single `nodeId` or a `rootNodeId` + any-descendant scope.

This is expressive enough for every wait/subscribe use case we've encountered
in the MotionCatalyst test suite, and strict enough that we never `eval` a
caller-supplied string on the Dispatcher.

### 7.3 Subscription lifecycle

```
Client: wpf_subscribe({ predicate, rootNodeId?, ttlSeconds? })
Server: { subscriptionId: "sub-42", treeVersion: 1234 }

... tree mutates ...

Server → Client (MCP notification):
  method: "wpf_subscription"
  params: { subscriptionId: "sub-42", treeVersion: 1235,
            matches: [ { nodeId: "0:17", snapshot: {...} } ] }

Client: wpf_unsubscribe({ subscriptionId: "sub-42" })
Server: { ok: true }
```

TTL default 300 s, max 3600 s. Subscription capped at 32 per session; older
ones evicted LRU with a `wpf_subscription_evicted` notification.

### 7.4 Dispatcher pumping

`wpf_pump_dispatcher({ untilPriority })` is a deterministic way to let queued
Dispatcher work finish. Implementation: post a sentinel at the given priority,
wait for its completion.

```
wpf_pump_dispatcher({ untilPriority: "ContextIdle" })
  → completes only after every Render/Input/Loaded/DataBind/etc. queued at
    higher priority has run.
```

Used internally by every mutation tool before returning (so tool semantics are
"operation + UI quiescent"). Exposed as an MCP tool primarily for diagnostic
scenarios.

### 7.5 Virtual clock

`SnoopAgentOptions.InstallVirtualClock = true` replaces:

- `DispatcherTimer` — via a private-reflection hook into
  `DispatcherTimer._timeManager` (best-effort, single internal field).
- `Stopwatch` — via `TimeProvider`-aware wrappers in the Sync subsystem.
- `Task.Delay` and `CancellationTokenSource(TimeSpan)` — only if consumer code
  opts into `TimeProvider` APIs explicitly. We document this as a limitation;
  most production WPF apps will require selective consumer cooperation.

`wpf_advance_time({ ms })` advances the virtual clock; all registered timers
fire synchronously in order. Deterministic for animation tests, debounce/throttle,
idle timeouts.

---

## 8. Semantic Navigation Subsystem

### 8.1 ViewModel addressing

`wpf_find_by_viewmodel({ viewModelType, propertyPath?, value?, rootNodeId? })`

- Walks the visual tree, evaluates `DataContext` at each node.
- Type match: `element.DataContext?.GetType().FullName == viewModelType` OR
  `IsAssignableFrom` semantics if `viewModelType` ends with `+`.
- Optional predicate on the DataContext instance: resolves `propertyPath` via
  reflection (dotted path, no method calls, no indexers > depth 1), compares
  with `value`.
- Returns matching element(s) up to `maxResults` (default 10, max 100).

Useful for: *"find the button displaying the currently selected Session"* →
`{ viewModelType: "SessionVm", propertyPath: "IsSelected", value: true }`.

### 8.2 Command tracing

`wpf_trace_command({ commandPath?, commandName? })`

- `commandPath`: dotted path from root DataContext (`MainVm.Recording.StartCmd`).
- `commandName`: for `RoutedCommand` lookups by name.
- Returns: array of `{ nodeId, elementType, commandParameter, canExecuteNow }`.

Use case: *"what UI invokes StartRecording?"*. Also: find dead command
bindings (elements whose `Command` resolves to null).

### 8.3 Binding resolution

`wpf_resolve_binding({ nodeId, propertyName })`

Returns a rich binding diagnostic:

```json
{
  "binding": {
    "path": "Recording.Session.Name",
    "mode": "TwoWay",
    "source": { "kind": "DataContext", "type": "MainVm" },
    "converter": "NameToTitleConverter",
    "validationRules": [ "StringNotEmpty" ]
  },
  "evaluation": {
    "resolvedSource": { "nodeId": null, "kind": "DataContext" },
    "intermediateValues": [
      { "segment": "Recording", "type": "RecordingVm", "isNull": false },
      { "segment": "Session",   "type": "SessionVm",   "isNull": true }
    ],
    "finalValue": null,
    "hasErrors": true,
    "errors": [ "Session is null (Recording.Session)" ]
  }
}
```

This is diagnostic gold — the current v3 `wpf_get_binding_info` returns path +
status; v4 returns the full evaluation trace.

---

## 9. Visual Capture Subsystem

### 9.1 WPF rendering

Default strategy: `RenderTargetBitmap` on a `Visual` (public API). Fast, exact,
headless-safe for pure-WPF content.

Limitation (unchanged from v3): D3D-hosted content (`D3DImage`,
`SharpDXElement`-style surfaces, WPF-interop with Direct3D) returns black.

### 9.2 D3D extensibility hook

New interface in `SnoopWPF.Agent.Contracts`:

```csharp
public interface ISnoopVisualCapture
{
    bool TryCapture(Visual visual, out byte[] bgraPixels,
                     out int width, out int height);
}
```

Consumer apps (MotionCatalyst) register an implementation:

```csharp
SnoopAgent.StartCoLocated(this, new SnoopAgentOptions {
    VisualCaptureHandlers = {
        (typeof(VideoViewer), new D3DVideoCaptureHandler())
    }
});
```

The capture subsystem dispatches by element type before falling back to
`RenderTargetBitmap`. MotionCatalyst's `D3DVideoCaptureHandler` would call
into the existing video pipeline to grab the current back-buffer as BGRA.

### 9.3 Baseline diff

`wpf_visual_diff({ nodeId, baselineKey, tolerance? })`

- Baseline store: `%TEMP%\SnoopVisualBaselines\{processName}\{baselineKey}.png`
  (configurable root).
- Capture current: combined strategy from 9.1/9.2.
- Compare: perceptual diff via SkiaSharp (ΔE 2000 by default, SSIM optional).
  Returns `{ matches: bool, maxDelta: double, diffImage?: "image/png" base64 }`.
- `wpf_visual_diff_update({ baselineKey })` writes the current capture as the
  new baseline (explicit, never implicit).

---

## 10. Recording & Replay Subsystem

`wpf_record_start({ sessionName, includeTreeSnapshots? })` begins capturing:

- Every input strategy invocation: tool name, fidelity, nodeId, parameters,
  treeVersion before, treeVersion after.
- Optional per-step tree snapshots (expensive; default off).
- Every subscription event delivered.

`wpf_record_stop()` returns a framed JSON blob (base64-encoded in the MCP
response), also written to `%TEMP%\SnoopRecordings\{sessionName}-{uuid}.json`.

`wpf_replay({ blob? | path? })` replays:

- Deterministic by construction: each step waits for the pre-recorded
  `treeVersion` to be reached before replaying the input.
- Reports divergence (observed treeVersion != expected post-state) with
  node-level diff.

Use case: reproduce a Claude session flake; diff against a known-good
recording to isolate which tool call first diverges.

---

## 11. Security Model

All v3 security properties retained. Additions for v4:

1. **`EnableAutomation` separate from `EnableMutation`.** v3's `EnableMutation`
   guards `wpf_set_property`. v4 adds `EnableAutomation` as a strict superset:
   input tools check both flags. A consumer can allow property writes for
   debugging but deny input simulation.

2. **Per-fidelity caps.** `SnoopAutomationOptions.MaxFidelity` (enum, default
   `L2`). Consumers opt into L3/L4 explicitly. MotionCatalyst in CI gets L4;
   MotionCatalyst in a developer's interactive session might cap at L2.

3. **Hardware input audit.** Every L4 invocation logs to stderr with
   `fidelity=hardware`, `tool`, `nodeId`, and an operator-supplied
   `reason` parameter. Required field when `fidelity: "hardware"`.

4. **Predicate evaluation sandbox.** JSON DSL (7.2) has no eval path. Regex
   inputs capped at 1000 chars and compiled with `RegexOptions.Compiled |
   NonBacktracking` (net7+).

5. **Recording data hygiene.** Recordings include tool parameters; a
   redaction layer runs recording-side using the v3 keyword list.
   `wpf_record_start({ redactKeywords: [...] })` extends the defaults.

6. **Replay in co-located only.** `wpf_replay` refuses if the current process
   PID differs from the recording's source PID **unless**
   `allowCrossProcess: true` is set. Prevents accidentally replaying a
   destructive sequence against a different app.

---

## 12. Performance Targets

Measured on Windows Server 2022, x64, .NET 8, warm cache, single dispatcher.

| Operation | v3 (today) | v4 target | Mechanism |
|-----------|-----------|-----------|-----------|
| `wpf_get_session_info` | 2 ms | 2 ms | Unchanged |
| `wpf_find_elements` (100 nodes) | 5 ms | **0.3 ms** | Cached tree + indexed lookup |
| `wpf_get_visual_tree` (depth 5) | 15 ms | **1 ms** | Snapshot from cache |
| `wpf_click` (L0) | — | **< 1 ms** | Command execute |
| `wpf_click` (L1) | — | **1 ms** | Peer invoke |
| `wpf_type_text` (L0, 20 chars) | — | **< 1 ms** | SetValue |
| `wpf_type_text` (L3, 20 chars) | — | **3 ms** | 20× RawKeyboardInputReport |
| `wpf_wait_until` (predicate fires immediately) | — | **< 1 ms** | TCS completion |
| `wpf_capture_screenshot` (window, pure WPF) | 50 ms | **5 ms** | RenderTargetBitmap, optimized |
| `wpf_capture_screenshot` (window, D3D via hook) | 50 ms | **10 ms** | Consumer hook |
| `wpf_visual_diff` (1 MP, SSIM) | — | **30 ms** | SkiaSharp ΔE |
| Subscription event latency | — | **< 5 ms** | Dispatcher → transport |

Budget rationale: every in-process tool should complete in under a frame
(16 ms @ 60 Hz) so we can chain 5–10 tool calls per UI frame if Claude
pipelines requests.

---

## 13. MCP Tool Inventory

### 13.1 Retained from v3

All 15 tools carry forward unchanged. `wpf_set_property` gains a `fidelity`
parameter for input-like properties (`Text`, `IsChecked`, `Value`) that can
dispatch to an input strategy when requested.

### 13.2 New in v4

**Input (7 tools)**
- `wpf_click(nodeId, fidelity?, modifiers?, button?, reason?)`
- `wpf_double_click(nodeId, fidelity?, button?)`
- `wpf_right_click(nodeId, fidelity?)`
- `wpf_type_text(nodeId, text, fidelity?, clearFirst?)`
- `wpf_send_keys(chord, fidelity?)` — single chord, e.g. `"Ctrl+Shift+P"`
- `wpf_drag(fromNodeId, toNodeId | toPoint, fidelity?)`
- `wpf_hover(nodeId, fidelity?)`
- `wpf_scroll(nodeId, direction, amount?, fidelity?)`
- `wpf_focus(nodeId)`

**Sync (5 tools)**
- `wpf_wait_until(predicate, minVersion?, timeoutMs?, rootNodeId?)`
- `wpf_subscribe(predicate, rootNodeId?, ttlSeconds?)`
- `wpf_unsubscribe(subscriptionId)`
- `wpf_pump_dispatcher(untilPriority?)`
- `wpf_advance_time(ms)`

**Semantic (3 tools)**
- `wpf_find_by_viewmodel(viewModelType, propertyPath?, value?, rootNodeId?, maxResults?)`
- `wpf_trace_command(commandPath? | commandName?)`
- `wpf_resolve_binding(nodeId, propertyName)`

**Capture (3 tools)**
- `wpf_capture_region(rect, includeD3D?)` — capture by screen-space rect
- `wpf_visual_diff(nodeId, baselineKey, tolerance?, returnDiffImage?)`
- `wpf_visual_diff_update(nodeId, baselineKey)`

**Recording (3 tools)**
- `wpf_record_start(sessionName?, includeTreeSnapshots?)`
- `wpf_record_stop()`
- `wpf_replay(blob? | path?, allowCrossProcess?)`

**Lifecycle (1 tool)**
- `wpf_pump_until_idle(timeoutMs?)` — convenience for
  `wpf_pump_dispatcher({ untilPriority: "ApplicationIdle" })`

Total new tools: **22**. Combined v3 + v4: **37**.

### 13.3 Tool schema excerpts

```
wpf_click:
  Input:
    nodeId: string
    fidelity?: "auto" | "auto-escalate" | "command" | "peer" |
               "event" | "input" | "hardware"       (default "auto")
    button?: "left" | "right" | "middle"             (default "left")
    modifiers?: string[]                              e.g. ["Ctrl","Shift"]
    reason?: string                                   required if fidelity="hardware"
  Output:
    chosenFidelity: string
    treeVersionBefore: long
    treeVersionAfter: long
    elapsedMs: double

wpf_wait_until:
  Input:
    predicate: object (predicate DSL, §7.2)
    minVersion?: long                                 default 0
    timeoutMs?: int                                   default 5000
    rootNodeId?: string
  Output:
    matched: boolean
    treeVersion: long
    matches: [ { nodeId, snapshot } ]                 (first match only)
    timedOut: boolean

wpf_subscribe:
  Input:
    predicate: object
    rootNodeId?: string
    ttlSeconds?: int                                  default 300
  Output:
    subscriptionId: string
    treeVersion: long                                 version at subscription time

Notification: "wpf_subscription"
  Params:
    subscriptionId: string
    treeVersion: long
    matches: [ { nodeId, snapshot } ]                 (all current matches in rootNodeId)

wpf_find_by_viewmodel:
  Input:
    viewModelType: string                              e.g. "MainVm", "SessionVm+"
    propertyPath?: string                              e.g. "IsSelected"
    value?: any                                        JSON-primitive
    rootNodeId?: string
    maxResults?: int                                   default 10, max 100
  Output:
    matches: [ { nodeId, elementType, dataContextType } ]
    totalCount: int
    truncated: boolean
```

### 13.4 Error taxonomy (delta)

New error codes on top of v3:

- `FidelityNotAvailable` — requested `fidelity` can't be satisfied
  (e.g. L4 in injection mode, L0 on an element with no Command).
- `CommandDisabled` — L0 picked, `CanExecute` returned false.
- `NoInputSource` — L2/L3 required, element has no `PresentationSource`
  (not parented, no `HwndSource`).
- `InternalApiMissing` — an `UnsafeAccessor` target missing at runtime.
  Includes the accessor name in the error suggestion.
- `PredicateInvalid` — JSON DSL parsed but references an unknown op/property.
- `AutomationDisabled` — `EnableAutomation: false`.
- `HardwareInputRequiresReason` — `fidelity: "hardware"` with no `reason`.
- `FidelityCapExceeded` — requested level > `MaxFidelity`.
- `VirtualClockNotInstalled` — `wpf_advance_time` called without the option.
- `SubscriptionLimit` — >32 active, oldest LRU evicted.

Every error includes a `suggestion` string — for input errors, the suggestion
points at the fallback fidelity the caller could try.

---

## 14. User Stories

Prefix `US-AUTO-`. Numbered to avoid overlap with v3's US-001..US-024.

### Phase 0: Spikes (M1)

- **US-AUTO-001**: Co-located hosting spike
- **US-AUTO-002**: `UnsafeAccessor` spike — verify every internal API lands
- **US-AUTO-003**: D3D capture hook spike against MotionCatalyst's video viewer

### M1: Foundation

- **US-AUTO-010**: `SnoopWPF.Agent.Input` project + `IInputStrategy` interface
- **US-AUTO-011**: `SnoopAgent.StartCoLocated` entry point
- **US-AUTO-012**: `SnoopAutomationOptions` + option plumbing
- **US-AUTO-013**: `WpfInternals` shim with accessor fallback
- **US-AUTO-014**: CI matrix (Windows Server 2022 / Windows 11, net6/net8)

### M2: L0/L1 input

- **US-AUTO-020**: `CommandStrategy`
- **US-AUTO-021**: `PeerStrategy`
- **US-AUTO-022**: `InputStrategySelector` (auto + cascade)
- **US-AUTO-023**: `wpf_click`, `wpf_double_click`, `wpf_right_click` tools
- **US-AUTO-024**: `wpf_type_text` (L0/L1 paths)
- **US-AUTO-025**: `wpf_focus`
- **US-AUTO-026**: `EnableAutomation` gating + L4 reason requirement

### M3: Deterministic sync

- **US-AUTO-030**: `TreeVersionTracker` — hook into visual tree changes
- **US-AUTO-031**: Watched-property registration
- **US-AUTO-032**: `WaitUntilBroker` + `wpf_wait_until`
- **US-AUTO-033**: `SubscriptionManager` + SSE notifications
- **US-AUTO-034**: `wpf_pump_dispatcher` + `wpf_pump_until_idle`
- **US-AUTO-035**: Virtual clock (best-effort, documented gaps)
- **US-AUTO-036**: Predicate DSL + evaluator

### M4: L2/L3 input + semantic

- **US-AUTO-040**: `EventStrategy` (L2 RaiseEvent)
- **US-AUTO-041**: `InputManagerStrategy` (L3)
- **US-AUTO-042**: `wpf_send_keys` (L3, chord parsing)
- **US-AUTO-043**: `wpf_drag` (L3 move sequence)
- **US-AUTO-044**: `wpf_hover`, `wpf_scroll`
- **US-AUTO-050**: `DataContextWalker` + `wpf_find_by_viewmodel`
- **US-AUTO-051**: `CommandReverseIndex` + `wpf_trace_command`
- **US-AUTO-052**: `BindingResolver` + `wpf_resolve_binding`

### M5: Capture + recording

- **US-AUTO-060**: `ISnoopVisualCapture` interface + registration
- **US-AUTO-061**: D3D reference handler in sample app
- **US-AUTO-062**: `wpf_capture_region`
- **US-AUTO-063**: Baseline store + `wpf_visual_diff` + `_update`
- **US-AUTO-070**: `SessionRecorder`
- **US-AUTO-071**: `SessionReplayer` with divergence reporting
- **US-AUTO-072**: `wpf_record_start/stop/replay` tools
- **US-AUTO-080**: `SendInputStrategy` (L4) — gated behind `MaxFidelity`

### M6: Release

- **US-AUTO-090**: NuGet `SnoopWPF.Agent.Automation` package
- **US-AUTO-091**: README with tier-selection decision tree
- **US-AUTO-092**: Sample-app showcase scenarios per subsystem
- **US-AUTO-093**: Migration guide for v3 consumers
- **US-AUTO-094**: CI green on release matrix

---

## 15. Milestones

```
M1 Foundation            US-AUTO-001..014          ~2 weeks design-ready
M2 L0/L1 input           US-AUTO-020..026          ~1 week after M1
M3 Deterministic sync    US-AUTO-030..036          ~2 weeks; can parallel M4
M4 L2/L3 + semantic      US-AUTO-040..052          ~3 weeks
M5 Capture + recording   US-AUTO-060..080          ~2 weeks
M6 Release               US-AUTO-090..094          ~1 week
```

(Timings are for architecture-level gating; bead-level sizing lives in
`BEADS.md` when US-AUTO-* are expanded.)

---

## 16. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Internal `RawInputReport` constructors change across .NET patches | Medium | High (L3 disabled) | Accessor test suite in CI against every supported runtime; graceful L3 disable; clear error surface. |
| Virtual clock can't hook every timer source | High | Medium | Document the gap; recommend `TimeProvider` adoption in consumer code; scope the option to opt-in. |
| D3D capture hook inverts surface orientation / color space | Medium | Medium | `ISnoopVisualCapture` contract specifies BGRA top-down; add unit test with a known pattern. |
| Tree version hooks leak handlers, slow tree mutations | Medium | High (perf) | Weak references + lazy registration; perf test asserts ≤ 1 µs per mutation for 1000 watched nodes. |
| L3 races with real input from user | Low (co-located + headless usual) | Low | Document; recommend `--headless` during automation; L4 is the only one that can truly race. |
| Predicate DSL regex DoS | Low | Medium | `NonBacktracking` option; input caps; CI fuzz with [AFL-style] inputs. |
| Recording blob size explodes with `includeTreeSnapshots` | High if enabled | Medium | Default off; chunked gzip; max 100 MB with explicit override. |
| `UnsafeAccessor` not available on older targets | Low | Low | `net6.0-windows` fallback uses reflection; gated by `#if NET8_0_OR_GREATER`. |
| Co-located mode stdout collision with app's own logging | Medium | High | Require `stderr` for logs; assert `Console.Out.Redirected` at start; document. |
| Injection mode L3 type-identity mismatch (target's `RawInputReport` ≠ ours) | Medium | High (L3 injection fails) | L3 explicitly reduced/disabled in injection mode per 5.1; no attempt to marshal internal types across load contexts. |

---

## 17. Consumer integration notes (MotionCatalyst-specific)

Non-normative — documents the concrete adoption path for the reference consumer.

1. **`App.xaml.cs` change**: on `--mcp-stdio`, call `SnoopAgent.StartCoLocated`
   with `EnableAutomation = true`, `EnableMutation = true`,
   `MaxFidelity = L4`.

2. **`.mcp.json`**: replace `motioncatalyst-ui` entry with direct
   `MotionCatalyst.exe --mcp-stdio --headless`. Delete
   `Tools/mcp-flaui-wrapper`.

3. **`D3DVideoCaptureHandler`**: new class in `MotionCatalyst.Video` that
   implements `ISnoopVisualCapture` by borrowing from the existing video
   pipeline's back-buffer-readout path. Registered via `SnoopAgentOptions
   .VisualCaptureHandlers`.

4. **Existing FlaUI test infra** (`InitialForce.FlaUI.Extensions`,
   `MotionCatalyst.Test.UI.Infra`): stays. SpecFlow steps migrate at their own
   pace. Recommend replacing step by step, starting with the hottest scenarios
   (login, session creation). A shim wrapper class `McpDriver` can expose the
   new tools under the existing `AppSession` / `FlaUIActionCatalog` API
   surface to make incremental migration mechanical.

5. **CI matrix**: add a job that runs the MCP tool suite in headless mode and
   asserts p99 budgets from §12. Block-merge on the p99 regression gate.

6. **Feature flag for rollback**: env var `MC_MCP_BACKEND=flaui|snoop` lets
   operators switch. Default to `snoop` once M2 lands; keep the FlaUI path
   green in CI until the UI test suite migration completes.

---

## 18. Open Questions

1. **NuGet packaging split** — one `SnoopWPF.Agent` package with automation
   included, or a separate `SnoopWPF.Agent.Automation` package that depends
   on the base? Current draft assumes separate (cleaner v3/v4 boundary,
   smaller dep footprint for inspection-only consumers).

2. **Subscriptions over injection mode** — the pipe protocol doesn't currently
   support server-initiated frames. Do we extend the pipe protocol (adds
   scope to the Injection Mode delivery), or make subscriptions
   co-located-only in v1? Leaning co-located-only; adds a row to the
   capability matrix.

3. **L4 in MotionCatalyst CI** — is SendInput ever actually needed for our
   scenarios, or does L3 cover 100%? If L3 covers 100%, `MaxFidelity = L3`
   and we skip L4 implementation entirely for now.

4. **SkiaSharp dependency footprint** — SkiaSharp is ~30 MB. For capture-only
   consumers that's acceptable; for inspection-only it's wasteful.
   Recommendation: soft reference via optional nuget metapackage
   `SnoopWPF.Agent.Automation.Skia`; core capture ships with a
   pure-managed per-pixel diff fallback.

5. **Open-sourcing posture** — companion plan (`wpf-mcp/docs/plans/snoop-
   integration-plan.md`) asks whether the fork is public-OSS. If yes, v4
   design constraints shift (no MotionCatalyst-specific assumptions in
   non-`MotionCatalyst-*` projects; the `D3DVideoCaptureHandler` example
   moves to a consumer repo).

6. **Predicate DSL expressiveness** — is "property + op + value with and/or/not"
   enough, or do we need arithmetic / function calls? Starting minimal;
   upgrade path is a strict superset. Likely enough for v1.

7. **CLR isolation for `UnsafeAccessor` in injection mode** — if injected into
   a .NET 6 app from a .NET 8 agent assembly, does `UnsafeAccessor` target
   the right type? Needs spike (US-AUTO-002).

---

## 19. Appendix

### A. WPF internal API reference

All gated through `WpfInternals` in `SnoopWPF.Agent.Input`. Version:
`.NET 8.0.15` and `.NET 6.0.35` (current LTS). CI runs the accessor test
against each patch release in the matrix.

```
namespace SnoopWPF.Agent.Input.Internals;

internal static class WpfInternals
{
    // L3 mouse
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern RawMouseInputReport ctor_RawMouseInputReport(
        InputMode mode, int timestamp, PresentationSource inputSource,
        RawMouseActions actions, int x, int y, int wheel, IntPtr extraInformation);

    // L3 keyboard
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern RawKeyboardInputReport ctor_RawKeyboardInputReport(
        PresentationSource inputSource, InputMode mode, int timestamp,
        RawKeyboardActions actions, int scanCode, bool isExtendedKey,
        bool isSystemKey, int virtualKey, IntPtr extraInformation);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern InputReportEventArgs ctor_InputReportEventArgs(
        InputDevice inputDevice, RawMouseInputReport report);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern InputReportEventArgs ctor_InputReportEventArgsKeyboard(
        InputDevice inputDevice, RawKeyboardInputReport report);

    // HwndSource from HWND (for window-scoped input synthesis)
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod,
                    Name = "CriticalFromHwnd")]
    public static extern HwndSource CriticalFromHwnd(IntPtr hwnd);
}
```

All accessors are **conditionally compiled** with `#if NET8_0_OR_GREATER`;
net6 fallback uses a cached `ConstructorInfo.Invoke` path.

### B. Comparison with existing frameworks

| Feature | FlaUI / UIA3 | WinAppDriver | TestStack.White | **SnoopWPF.Agent.Automation v4** |
|---------|-------------|-------------|-----------------|----------------------------------|
| Cross-process | Yes | Yes | Yes | No (co-located primary) |
| Works in headless | No | No | No | **Yes** |
| Per-call fidelity | No | No | No | **Yes (5 levels)** |
| Sub-ms reads | No | No | No | **Yes** |
| Push subscriptions | No | No | No | **Yes** |
| Virtual clock | No | No | No | **Yes (opt-in)** |
| ViewModel addressing | No | No | No | **Yes** |
| Record/replay | No | No | No | **Yes** |
| D3D capture | No | No | No | **Yes (hook)** |
| Native MCP | No | No | No | **Yes** |

### C. Glossary

- **Co-located mode** — the target WPF app hosts the MCP server in-process.
- **Fidelity tier** — one of L0/L1/L2/L3/L4 semantic levels of input simulation.
- **Tree version** — monotonic counter, bumped on any watched tree mutation.
- **Predicate DSL** — JSON object form describing an element-matching
  expression, evaluated on the Dispatcher without `eval`.
- **Virtual clock** — an `ITimeProvider` shim that makes timer-based UI logic
  deterministic.
- **Semantic tool** — an MCP tool that operates in domain terms (ViewModel,
  Command, Binding) rather than visual-tree terms (element, child, property).

---

*End of PRD v4. Review and BEADS expansion follows.*
