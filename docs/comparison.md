# How snoopwpf compares

This page compares InitialForce's snoopwpf fork (the one publishing `InitialForce.SnoopAgent`)
against seven other tools that inspect or automate WPF applications. The goal is an honest
picture: every alternative does something well; the right tool depends on the job.

## Background

When a WPF application behaves unexpectedly, there are two distinct problems an engineer might
be trying to solve. The first is **inspection**: understanding why the UI looks or behaves the
way it does — which property value is driving a layout, what the binding chain resolves to, what
resource dictionary entry wins a key lookup, which trigger just fired. The second is
**automation**: driving the application programmatically to test workflows or verify state changes
across a sequence of interactions.

Most tools in this space address automation, not inspection. They sit on top of the Windows
UI Automation (UIA) API, which exposes a subset of control state — the names, types, bounding
rectangles, and a fixed set of control patterns that Windows standardizes across all UI
frameworks. UIA is sufficient for driving an application. It is not sufficient for answering
questions like "why is this TextBox blank?" or "which resource dictionary is supplying this
brush and is it being shadowed by a closer entry?"

SnoopWPF.Agent (this fork) adds an MCP-native surface to the Snoop inspection engine, making
inspection-level answers accessible to AI agents without requiring human interaction with the
Snoop GUI. This comparison page exists to help you decide when that capability matters and when
a simpler tool serves better.

### How to read this page

- The [TL;DR table](#tldr) gives a four-dimension summary per alternative.
- [What each alternative does well](#what-each-alternative-does-well) describes each tool
  fairly, with links.
- The [full capability matrix](#full-capability-matrix) covers ~20 features with footnoted
  qualifications.
- [Honest weaknesses](#honest-weaknesses) lists five areas where a different tool wins.
- [Picking the right tool](#picking-the-right-tool) maps common scenarios to recommendations.
- [Citations](#citations) records the source and verification date for each maintenance status
  claim.

---

## TL;DR

| Alternative | Introspection depth | Attach friction | Mutability | MCP native | Verdict |
|---|---|---|---|---|---|
| **Upstream Snoop WPF** | ✅ Full WPF tree | ⚠️ Manual GUI attach | ⚠️ GUI only | ❌ | Best human-facing inspector; no agent API |
| **FlaUI** | ⚠️ UIA tree only | ✅ Zero injection | ⚠️ UIA patterns only | ❌ | Solid cross-framework automation; no binding-level insight |
| **Raw UIA3 (COM)** | ⚠️ UIA tree only | ✅ Zero injection | ⚠️ UIA patterns only | ❌ | Maximum OS compatibility; verbose COM surface |
| **WinAppDriver** | ⚠️ UIA tree only | ⚠️ Separate service | ⚠️ WebDriver gestures | ❌ | Archived; do not start new projects on it |
| **Appium Windows Driver** | ⚠️ UIA tree only | ⚠️ Appium + driver stack | ⚠️ WebDriver gestures | ❌ | Active successor to WinAppDriver; mobile-centric workflow |
| **Playwright** | ❌ No native WPF | ✅ Zero injection (web/Electron only) | ✅ Full browser | ❌ | Wrong layer for pure WPF apps |
| **TestStack/White** | ⚠️ UIA tree only | ✅ Zero injection | ⚠️ UIA patterns only | ❌ | Archived; use FlaUI instead |
| **snoopwpf (this fork)** | ✅ Visual + logical + UIA tree; binding chains; DataContext; triggers | ⚠️ Injection or NuGet embed | ✅ DP mutation via SetCurrentValue | ✅ | Only tool with native MCP surface and binding-chain resolution |

---

## What each alternative does well

### Upstream Snoop WPF

[snoopwpf/snoopwpf](https://github.com/snoopwpf/snoopwpf) —
[Latest release: v6.0.0, May 2025](https://github.com/snoopwpf/snoopwpf/releases/tag/v6.0.0)

The canonical WPF inspector. Upstream Snoop provides the most complete human-operated GUI for
examining the visual tree, live property values, and binding errors — with a polished interface
that lets developers navigate large trees interactively. Trigger enumeration, resource
inspection, and binding-error highlighting are all well-integrated into the GUI, and the project
is actively maintained by a community of WPF practitioners. Version 6.0 added a revamped
diagnostics panel and improved DataTemplate support. It is the right choice when a human
developer needs to debug a live app manually without writing any code. The InitialForce fork
derives from this codebase and adds an MCP-over-stdio transport layer on top of the same
inspection engine — so the two are broadly equivalent in inspection depth, but only the fork
exposes that depth programmatically.

### FlaUI

[FlaUIInc/FlaUI](https://github.com/FlaUIInc/FlaUI) —
[Latest release: v4.0.0, June 2025](https://github.com/FlaUIInc/FlaUI/releases/tag/v4.0.0)

FlaUI is the most actively maintained UIA wrapper for .NET. It covers WPF, WinForms, WinUI 3,
and Win32 dialogs through a single consistent API, making it the practical choice whenever a
test suite needs to cross UI-framework boundaries. Its event model (`AddAutomationEventHandler`)
delivers real push notifications from the OS automation framework, so a test harness can react
to structural changes — element added, element removed, property changed — without any polling
loop. This push model is genuinely superior to polling for event-driven architectures. The
experimental [FlaUI.WebDriver](https://github.com/FlaUIInc/FlaUI/tree/main/src/FlaUI.WebDriver)
shim (February 2026) extends FlaUI with a W3C WebDriver endpoint, making it compatible with any
Selenium-speaking test runner. FlaUI requires zero injection and attaches to any running process
through the public UIA COM interfaces, which means it works in environments where DLL injection
is prohibited by policy. Its key limitation for WPF-specific work is that it sees only the UIA
automation tree, not the WPF visual or logical tree — so DP properties, binding expressions,
DataContext objects, and resource dictionaries are all opaque to it.

### Raw Windows UIA3 (COM)

[UI Automation — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification)

The OS-level API that FlaUI, WinAppDriver, and Appium all sit on top of. Using it directly gives
maximum control over automation event subscriptions, property filtering, and custom control
patterns — with no additional dependencies beyond the Windows SDK. It is always current because
it is an OS API, not a versioned package: any process that implements `IUIAutomationProvider` is
automatically visible. This makes it the correct foundation for long-lived system tooling where
a third-party dependency chain is a liability. It is also the only way to register custom
control patterns or extend the automation property set beyond what a wrapper library exposes.
The cost is verbosity: identifying an element requires navigating a tree of `IUIAutomationElement`
COM objects, and every waiter or retry must be implemented by the caller. There is no built-in
concept of a locator strategy with fallbacks, a polling waiter with a timeout, or a screenshot
crop by element bounds.

### WinAppDriver

[microsoft/WinAppDriver](https://github.com/microsoft/WinAppDriver) —
[Latest release: v1.2.1, 2020](https://github.com/microsoft/WinAppDriver/releases/tag/v1.2.1)

Microsoft's Selenium-over-UIA bridge. WinAppDriver exposed WPF, WinForms, and UWP apps through
the W3C WebDriver protocol, letting teams reuse Selenium tooling for desktop automation.
Its chief strength was broad ecosystem compatibility: any language with a Selenium client library
could drive Windows apps, and the wire protocol was already well-understood by test infrastructure
teams that had invested in Selenium. It also enabled parallel test execution via the standard
WebDriver grid mechanism. However, the project has had no new commits since 2020 and is
effectively unmaintained — open issues related to Windows 11 compatibility and modern .NET targets
remain unresolved. Do not start new projects on WinAppDriver. Existing suites should plan
migration to Appium Windows Driver, which is a largely compatible drop-in for the WebDriver
surface while adding ongoing maintenance.

### Appium Windows Driver

[appium/appium-windows-driver](https://github.com/appium/appium-windows-driver) —
[Latest release: v3.x, 2025](https://github.com/appium/appium-windows-driver/releases)

The active, community-maintained successor to WinAppDriver under the Appium 3 umbrella. It
tunnels through WinAppDriver under the hood but receives ongoing maintenance, Appium 3
compatibility, and cross-platform client support. Teams that already invest in Appium for mobile
testing (iOS, Android) can extend that infrastructure to Windows desktop with minimal additional
tooling — the same Appium server, the same client libraries, the same CI configuration patterns.
This platform convergence is a genuine operational advantage for organizations running large
heterogeneous test fleets. The WebDriver wire protocol is the same as WinAppDriver's, so the
UIA-level capability ceiling is unchanged: inspection is limited to what UIA exposes, and
property mutation goes through UIA patterns rather than the WPF DP system. Setup requires
running both the Appium server and the Windows Driver, adding more moving parts than a pure
.NET solution like FlaUI.

### Playwright

[microsoft/playwright](https://github.com/microsoft/playwright) —
[Latest release: current, ongoing](https://github.com/microsoft/playwright/releases)

Playwright is the gold standard for browser and Electron automation. It offers reliable
cross-browser coverage (Chromium, Firefox, WebKit), rich locator strategies backed by the
Accessibility tree and CSS/text selectors, built-in network mocking and request interception, and
first-class tracing and video capture. For Electron-based desktop applications, Playwright is the
correct tool — the same APIs that automate a Chrome tab also work against an Electron window with
full DevTools protocol access. For hybrid teams building web plus Electron plus a WPF admin tool,
Playwright handles the web and Electron portions better than any alternative in this list, while
a tool like FlaUI or snoopwpf covers the WPF portion. For pure WPF apps, Playwright has no
applicable code path: WPF windows are not rendered in a browser engine and Playwright's CDP
(Chrome DevTools Protocol) client cannot attach to them. Including Playwright in this comparison
is intentional — it is often the answer to the question "can Playwright handle this?" and the
correct answer for WPF is no, redirect to FlaUI or this fork.

### TestStack/White

[TestStack/White](https://github.com/TestStack/White) — archived ~2016

White was the dominant .NET UIA wrapper before FlaUI emerged. It covered WPF, WinForms, and
Win32 through a fluent API that was ahead of its time in 2008: element finders with retry logic,
a typed control hierarchy (`Button`, `TextBox`, `ListBox`) that shielded tests from raw
`IUIAutomationElement` COM, and an `Application` abstraction that managed process lifecycle.
Those patterns are directly reflected in FlaUI's design, which can be thought of as White rebuilt
on a modern .NET foundation with active maintenance. Although White is archived, a large number
of legacy test suites still run on it because the API surface is stable and the .NET Framework
target is compatible. For any new project or major investment in expanding an existing suite,
FlaUI is the direct functional successor and the correct choice. White is included in this
comparison because engineers working on applications that already have White-based tests will
encounter it, and the migration path to FlaUI is well-understood.

---

## Full capability matrix

Symbols: ✅ full support / ⚠️ partial or workaround needed / ❌ not supported

| Feature | snoopwpf (this fork) | Upstream Snoop | FlaUI | Raw UIA3 | WinAppDriver | Appium Win | Playwright | White |
|---|---|---|---|---|---|---|---|---|
| Visual tree | ✅ | ✅ | ⚠️ [1] | ⚠️ [1] | ⚠️ [1] | ⚠️ [1] | ❌ | ⚠️ [1] |
| Logical tree | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Automation tree (UIA) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Property read | ✅ | ✅ | ⚠️ [2] | ⚠️ [2] | ⚠️ [2] | ⚠️ [2] | ❌ | ⚠️ [2] |
| Property MUTATE | ✅ [3] | ⚠️ [4] | ⚠️ [5] | ⚠️ [5] | ⚠️ [5] | ⚠️ [5] | ❌ | ⚠️ [5] |
| Binding path resolve | ✅ | ✅ [4] | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| DataContext inspect | ✅ | ✅ [4] | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Style/trigger enum | ✅ | ✅ [4] | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Resource lookup (with shadow) | ✅ | ✅ [4] | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Behavior enum (Blend) | ✅ | ✅ [4] | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Screenshot (element) | ✅ | ⚠️ [4] | ⚠️ [6] | ❌ | ❌ | ⚠️ [6] | ✅ | ❌ |
| Screenshot (window) | ✅ | ✅ | ⚠️ [6] | ❌ | ⚠️ [6] | ⚠️ [6] | ✅ | ❌ |
| Screenshot as blob ref (no context bloat) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Keyboard input | ✅ [7] | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Click / invoke | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| List virtualization handling | ✅ [8] | ❌ | ⚠️ [9] | ⚠️ [9] | ⚠️ [9] | ⚠️ [9] | ❌ | ⚠️ [9] |
| Hot-reload friendly | ✅ | ✅ | ✅ | ✅ | ⚠️ | ⚠️ | ✅ | ❌ |
| MCP native | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Protocol type | MCP stdio/pipe | None (GUI) | .NET API | COM API | WebDriver | WebDriver | WebDriver | .NET API |
| In-proc vs out-of-proc | Both [10] | Out (GUI) | Out | Out | Out | Out | Out | Out |
| Last release year | 2026 | 2025 | 2025 | N/A | 2020 | 2025 | 2026 | 2016 |
| Status | Active | Active | Active | OS built-in | Archived | Active | Active | Archived |

**Footnotes**

1. UIA tree only — does not distinguish WPF visual tree from logical tree. WPF-internal
   elements that do not override `UIElement.CreateAutomationPeer()` are invisible to UIA-based
   tools, including all elements whose AutomationPeer returns `null`. This typically includes
   decorators, adorners, and non-interactive panels that WPF renders visually but does not
   expose for accessibility purposes.
2. Only exposes UIA AutomationElement properties (Name, ControlType, BoundingRectangle,
   IsEnabled, etc.) plus the values exposed through specific control patterns (Value via
   `ValuePattern`, selection state via `SelectionPattern`, etc.). Arbitrary DependencyProperty
   values — background brushes, font sizes, margin/padding, binding expressions, DataContext
   types — are not accessible.
3. DP mutation via `DependencyObject.SetCurrentValue` — preserves existing TwoWay binding
   expressions on the target property. Requires `EnableMutation = true` in `SnoopAgentOptions`.
   A subset of property types is in the safe-settable list; see the
   [security model](security.md#typeconverter-safety) for details. See also
   [honest weaknesses](#honest-weaknesses) for the limitations of DP-layer mutation vs.
   UIA-pattern mutation.
4. Available in the Snoop GUI only; not accessible programmatically or via any API.
   The GUI does expose these features in a polished, interactive form — this is Snoop's primary
   use case and it excels at it. The limitation is that an AI agent or automated test harness
   cannot call into the Snoop GUI to retrieve these values programmatically.
5. Via UIA patterns only — `ValuePattern.SetValue` for text controls, `TogglePattern.Toggle`
   for checkboxes, `SelectionItemPattern.Select` for list items. These patterns go through the
   control's own UIA provider implementation and do NOT call `SetCurrentValue`. On most WPF
   controls, `ValuePattern.SetValue` will clear an active TwoWay binding on the target
   DependencyProperty because it sets the value at a layer below the WPF binding system.
6. Via OS-level `BitBlt` / `Graphics.CopyFromScreen` clipped to the element's bounding
   rectangle. Requires the element to be on-screen and unobscured. Does not handle DPI scaling
   on non-primary monitors without additional code. May capture overlapping windows if the
   element is partially covered.
7. Via `wpf_set_text_value` (TextBox/PasswordBox/RichTextBox), `wpf_set_check_state`
   (CheckBox/RadioButton), and `wpf_set_slider_value` (Slider/RangeBase) — all implemented
   with `SetCurrentValue` at the DP layer. Raw Win32 keystroke simulation (`SendInput`,
   `PostMessage WM_KEYDOWN`) is not provided; use FlaUI's `Keyboard` class for that need.
8. `wpf_select_item` with an integer index identifier calls `ItemContainerGenerator.ContainerFromIndex`
   followed by `BringIntoView` to materialize the virtualized container. The item is then
   selected via `SetCurrentValue` on `Selector.SelectedItemProperty`. Inspection-without-selection
   of virtualized items (reading properties from an off-screen item without changing selection)
   is not yet supported — see [honest weaknesses](#honest-weaknesses).
9. UIA cannot enumerate items that have not been realized by the `VirtualizingStackPanel` (or
   other virtualizing panel). The automation tree for a 10,000-item `ListBox` reports only the
   currently visible item containers. Workarounds involve programmatic scrolling to force
   realization, but these are fragile under re-virtualization.
10. NuGet co-located mode is in-proc (agent runs in the same process as the app, shares the
    WPF Dispatcher). Injection mode and brokered mode are out-of-proc (the `snoop-mcp.exe`
    injector loads into the target process address space, but the MCP server transport boundary
    is a named pipe to the external host).

---

## Honest weaknesses

Five places where a competing tool wins or partially wins over snoopwpf.

### 1. Injection required for external mode — FlaUI and UIA3 work zero-injection

FlaUI and raw UIA3 attach to any running Windows process through the published OS automation
APIs — no DLL injection, no process modification. Snoopwpf's external (injection) mode requires
injecting `snoop-mcp.exe` into the target process, which demands that the process be owned by
the current user and that certain anti-tamper mechanisms are absent. In high-security or
sandboxed environments, injection may be blocked outright. The NuGet co-located mode avoids
injection entirely but requires a recompile.

### 2. WPF-only — FlaUI and UIA3 cover WinForms, WinUI 3, and Win32 dialogs too

Snoopwpf's inspection engine is built on WPF internals: `DependencyObject`, `FrameworkElement`,
`VisualTreeHelper`, and the WPF resource system. None of these APIs exist in WinForms or WinUI 3.
An application with mixed UI technology — for example a WPF shell hosting legacy WinForms
panels via `WindowsFormsHost`, or a WinUI 3 migration in progress — cannot have the non-WPF
portions inspected by snoopwpf. FlaUI and raw UIA3 operate at the OS abstraction layer and
see all frameworks uniformly.

### 3. Brokered mode lifecycle not fully complete (as of April 2026)

The brokered mode, which allows a separate broker process to inject into an already-running app
and expose it over MCP, has some lifecycle tooling gaps. Certain broker-specific commands
(for example `mc_wait_for_recording` and the full encoding-pipeline polling path required for
multi-step async state machines) are deferred to a future milestone. As documented in
[shim-retained-flaui.md](shim-retained-flaui.md), two scenario families in the MotionCatalyst
integration continue to use FlaUI fallbacks because the shim cannot yet cover their specific
polling patterns. Applications that rely heavily on broker lifecycle coordination — particularly
those with multi-step async recording or encoding pipelines — should review that document before
committing to the brokered path. The active FlaUI fallback count is tracked against the M2 gate
(threshold: 5 active families) and is currently at 2, indicating the gap is bounded and
narrowing.

### 4. Polling-based change detection — FlaUI has push events

`wpf_poll_changes` returns structural deltas by comparing tree snapshots — the agent must call
it repeatedly to detect changes, and the minimum observable latency is one poll interval
(approximately 80–100 ms in practice). FlaUI's `AddAutomationEventHandler` registers an
OS-level callback that fires synchronously when an element is added, removed, or has a property
change event raised by the WPF automation peer. The difference matters for test architectures
where the harness must react immediately to UI changes — for example, a test that asserts a
spinner disappears within 200 ms of an operation completing, or a monitor that must log every
structural change in the tree in real time. FlaUI's push model has lower latency and does not
consume CPU cycles between events. Snoopwpf's `wpf_wait_for_property` tool is a convenience
wrapper that polls internally, which reduces the polling burden on the caller but does not
eliminate it.

### 5. DP mutation bypasses VM change notifications for some workflows

`wpf_set_property` and the L0 mutation tools (`wpf_set_text_value`, `wpf_set_check_state`,
etc.) use `DependencyObject.SetCurrentValue`, which preserves active TwoWay bindings and
triggers DP change notifications — but it does not simulate physical user input. Some
applications validate or transform input only in response to `TextBox.PreviewTextInput`,
`KeyDown`, or focus-change events, which are not fired by `SetCurrentValue`. UIA-based tools
(`ValuePattern.SetValue` in FlaUI and WinAppDriver) go through the control's own input
processing and raise these events, more closely reproducing what a real user does. If your
test coverage depends on event-driven validation that runs only on physical-input events,
prefer UIA-pattern–based mutation over DP-layer mutation.

---

## Picking the right tool

The table below maps five common scenarios to a tool recommendation with a brief rationale.
"Pick" is a primary recommendation; in mixed scenarios, combining two tools is sometimes the
right answer.

| Scenario | Pick | Rationale |
|---|---|---|
| **I own the source; CI test harness** | snoopwpf NuGet mode | Zero injection, in-process MCP server, full binding-chain diagnostics, CI-friendly stdio transport. Add `SnoopAgent.StartCoLocated()` behind an env-var flag so it only runs in CI and dev builds. |
| **Third-party legacy WPF app; debug once** | snoopwpf injection OR FlaUI | Injection gives deeper insight (binding chains, DataContext, triggers); FlaUI is the safer choice if the process is owned by a different user account, runs elevated, or has anti-tamper tooling active. |
| **Cross-framework automation (WPF + WinForms + Electron)** | FlaUI + Playwright | FlaUI covers WPF/WinForms/WinUI3 through a single .NET API; Playwright covers Electron and browser targets. Neither covers the other's domain; use both in the same test suite if the application spans frameworks. |
| **Push-event–driven watcher** | FlaUI (`AddAutomationEventHandler`) | OS-level push events fire synchronously without polling overhead. Suitable for harnesses that react to UI state changes at sub-100 ms latency. |
| **Runtime binding-chain debugging** | snoopwpf | The only tool that walks multi-step binding paths with per-segment live values, surfaces DataContext runtime types, identifies shadowed resource entries, and enumerates style and ControlTemplate triggers — all over a structured MCP API. |

### Notes on mixed scenarios

Several real applications require a pragmatic combination. A WPF host with embedded WinForms
panels (via `WindowsFormsHost`) can use snoopwpf for the WPF portions and FlaUI for the WinForms
panels — both attach to the same process simultaneously without conflict. An application under
active development that uses both `dotnet watch` hot reload and an AI coding agent can run in
NuGet mode during development (where the agent drives binding diagnostics) and FlaUI-based
SpecFlow scenarios in CI (where the test suite verifies end-to-end workflows). These modes are
complementary, not exclusive.

### What snoopwpf does not try to do

SnoopWPF.Agent is an inspection and light-mutation tool, not a general-purpose UI test runner.
It deliberately omits raw input simulation (mouse movement, physical keystrokes via `SendInput`),
cross-process coordination primitives (grid execution, parallel test sessions), and built-in
assertion libraries. For production test suite infrastructure — test discovery, parallel
execution, reporting, CI integration — use a testing framework (xUnit, NUnit, SpecFlow) with
FlaUI or Appium as the driver, optionally adding snoopwpf NuGet mode for binding-level
assertions where they add value over UIA-based checks.

The recommended pattern for teams building out a WPF test suite from scratch is:

1. FlaUI (or Appium) as the primary driver for workflow-level tests.
2. snoopwpf NuGet mode (conditional on `#if DEBUG` or an env var) for diagnostic sessions
   and binding-chain assertions during development.
3. Upstream Snoop GUI for ad-hoc interactive debugging by developers.

This layering gives each tool the scope it is optimized for.

---

## Methodology and accuracy

The claims in this document were verified against the tool repositories and documentation in
April 2026. Strong claims — particularly "only tool that" — are backed by a corresponding row
in the capability matrix. Where a claim involves a nuance or a partial capability, a footnote
explains the scope.

Maintenance status is determined by the most recent tagged release on the respective GitHub
repository, not by commit frequency alone, since some mature libraries release infrequently
but remain correct and well-maintained. The WinAppDriver "archived" classification reflects the
combination of no tagged release since 2020, open GitHub issues marked "no activity", and
Microsoft's own guidance directing users to Appium Windows Driver.

If you find a factual error — for example if a tool you use has added a capability listed as
❌ — please open an issue against [InitialForce/snoopwpf](https://github.com/InitialForce/snoopwpf)
with a link to the relevant documentation or release notes.

## Citations

Maintenance status and release dates used in the matrix above are sourced from the GitHub
releases pages listed here. All dates were verified in April 2026.

| Tool | Repository | Releases page | Last release |
|---|---|---|---|
| Upstream Snoop WPF | [snoopwpf/snoopwpf](https://github.com/snoopwpf/snoopwpf) | [Releases](https://github.com/snoopwpf/snoopwpf/releases) | v6.0.0 — May 2025 |
| FlaUI | [FlaUIInc/FlaUI](https://github.com/FlaUIInc/FlaUI) | [Releases](https://github.com/FlaUIInc/FlaUI/releases) | v4.0.0 — June 2025 |
| FlaUI.WebDriver | [FlaUIInc/FlaUI](https://github.com/FlaUIInc/FlaUI/tree/main/src/FlaUI.WebDriver) | Same repo | Experimental — February 2026 |
| Raw UIA3 | [Windows Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification) | OS built-in | N/A — always current |
| WinAppDriver | [microsoft/WinAppDriver](https://github.com/microsoft/WinAppDriver) | [Releases](https://github.com/microsoft/WinAppDriver/releases) | v1.2.1 — 2020 (no further commits) |
| Appium Windows Driver | [appium/appium-windows-driver](https://github.com/appium/appium-windows-driver) | [Releases](https://github.com/appium/appium-windows-driver/releases) | v3.x — 2025 |
| Playwright | [microsoft/playwright](https://github.com/microsoft/playwright) | [Releases](https://github.com/microsoft/playwright/releases) | Current — ongoing 2026 |
| TestStack/White | [TestStack/White](https://github.com/TestStack/White) | [Releases](https://github.com/TestStack/White/releases) | Archived ~2016 |
| snoopwpf (InitialForce) | [InitialForce/snoopwpf](https://github.com/InitialForce/snoopwpf) | [Releases](https://github.com/InitialForce/snoopwpf/releases) | 2026 (active) |
