# SnoopWPF to MCP Server + CLI: Transformation Plan

> Synthesized from 6 parallel deep-dive code reviews covering 305 source files

## Executive Summary

SnoopWPF is a WPF runtime inspector that injects a managed DLL into a target WPF process to inspect visual trees, properties, bindings, events, resources, and diagnostics. **The core inspection engine is already headless-capable** — the UI is a thin consumer layer. The transformation requires:

1. **A new IPC channel** (named pipes with StreamJsonRpc) between the injected agent and the host process
2. **A new MCP server** exposing 17 tools via the ModelContextProtocol C# SDK
3. **A new CLI** mirroring those tools as commands
4. **~200-300 lines of cleanup** to existing Snoop.Core (remove MessageBox calls, add DTO projections)
5. **~800-1200 lines of new code** for the IPC layer, MCP tools, and CLI commands

No existing engine logic needs rewriting from scratch.

---

## Current Architecture

```
Snoop.exe (GUI)                    Target WPF Process
  |                                    |
  |-- AppChooser (process picker)      |
  |-- WindowFinder (drag-to-target)    |
  |                                    |
  +-- InjectorLauncherManager          |
       |                               |
       +-- Snoop.InjectorLauncher.exe  |
            |                          |
            +-- GenericInjector.dll ----+--> SnoopManager.StartSnoop()
                (native C++ DLL)       |      |
                                       |      +-- SnoopUI window (WPF)
                                       |           |-- TreeService (visual/logical/automation)
                                       |           |-- PropertyInformation (live bindings)
                                       |           |-- DiagnosticContext (5 providers)
                                       |           |-- EventTracker (routed events)
                                       |           |-- VisualCaptureUtil (screenshots)
                                       |           +-- TreeExporter (XML export)
```

**Critical gap:** There is NO IPC between the Snoop host and the injected code. SnoopUI runs autonomously inside the target process. All inspection data stays in-process.

---

## Target Architecture

```
AI Agent / Claude / User
    |
    | MCP JSON-RPC (stdio) or CLI commands
    v
snoop-mcp / snoop-cli (host process, net8.0-windows)
    |
    | StreamJsonRpc over Named Pipes ("SnoopAgent_{pid}")
    |
    +-- SessionManager (per-PID connections)
    |
    +-- InjectorBridge --> Snoop.InjectorLauncher.exe --> GenericInjector.dll
                                                              |
                                                              v
                                                    Target WPF Process
                                                    SnoopAgentServer (JSON-RPC)
                                                        |-- TreeService
                                                        |-- PropertyInformation
                                                        |-- DiagnosticContext
                                                        |-- EventTracker
                                                        |-- VisualCaptureUtil
                                                        +-- ResourceInspector
```

---

## New Project Structure

```
Snoop.sln
|
|-- Snoop.Core/                      KEEP (multi-target net462;net6.0-windows)
|     Minor changes: remove MessageBox.Show(), add HeadlessAgent start target
|
|-- Snoop.Core.Headless/             NEW (netstandard2.0)
|     DTOs: NodeDto, PropertyDto, BindingInfoDto, DiagnosticDto, ResourceDto, etc.
|     IPC interface: ISnoopAgentRpc
|     Shared between host and injected agent (no WPF dependency)
|
|-- Snoop.InjectedAgent/             NEW (net462;net6.0-windows, UseWpf=true)
|     SnoopAgentEntryPoint (replaces SnoopUI for headless mode)
|     AgentRpcServer (StreamJsonRpc server over NamedPipeClientStream)
|     Wraps existing TreeService, PropertyInformation, DiagnosticContext, etc.
|     ILRepack to merge StreamJsonRpc deps (avoid conflicts with target app)
|
|-- Snoop.MCP/                       NEW (net8.0-windows)
|     MCP server using ModelContextProtocol SDK
|     17 tool classes organized by category
|     SessionManager, InjectorBridge
|     Stdio transport (default) + optional HTTP/SSE
|
|-- Snoop.CLI/                       NEW (net8.0-windows)
|     System.CommandLine-based CLI
|     Same SessionManager and IPC stack as Snoop.MCP
|     Output formatters: JSON, table, tree
|
|-- Snoop.InjectorLauncher/          KEEP UNCHANGED
|-- Snoop.GenericInjector/           KEEP UNCHANGED (native C++ DLL)
|-- Snoop/                           KEEP (existing GUI, optional)
|-- Snoop.Console/                   KEEP (existing console shim)
|-- Snoop.Core.Tests/                KEEP + extend with headless tests
```

### Dependency Graph

```
Snoop.MCP ---------> Snoop.Core.Headless (DTOs + ISnoopAgentRpc)
Snoop.CLI ---------> Snoop.Core.Headless
Snoop.InjectedAgent -> Snoop.Core.Headless + Snoop.Core (engine)
Snoop.Core.Headless -> (no deps, netstandard2.0)
```

Key rule: **Snoop.MCP and Snoop.CLI never reference Snoop.Core** — they only know about DTOs and the RPC interface. The WPF engine stays inside the injected agent.

---

## IPC Design: StreamJsonRpc over Named Pipes

### Why Named Pipes + StreamJsonRpc

- Works on both .NET Framework 4.6.2 (inside target) and .NET 8 (host)
- JSON-RPC aligns with MCP's own JSON-RPC foundation
- Supports request/response AND streaming (IAsyncEnumerable for events)
- No Windows Firewall prompts (unlike TCP)
- Pipe name `SnoopAgent_{pid}` provides natural per-process isolation

### Communication Flow

1. Host creates `NamedPipeServerStream("SnoopAgent_{pid}")`
2. Host injects `Snoop.InjectedAgent.dll` via existing GenericInjector, passing pipe name in TransientSettingsData
3. Injected agent connects `NamedPipeClientStream` to `SnoopAgent_{pid}`
4. Both sides attach `StreamJsonRpc.JsonRpc`
5. Host calls methods on `ISnoopAgentRpc` proxy
6. Agent marshals all calls to WPF Dispatcher, builds DTOs, returns them

### ISnoopAgentRpc Interface

```csharp
public interface ISnoopAgentRpc
{
    // Tree
    Task<NodeDto[]> GetTreeAsync(string treeType, string? rootId, int maxDepth, CancellationToken ct);
    Task<NodeDto?> FindElementAsync(string? name, string? typeName, string treeType, CancellationToken ct);
    Task<NodeDto[]> FindElementsAsync(string? typeName, string? name, int maxResults, CancellationToken ct);

    // Properties
    Task<PropertyDto[]> GetPropertiesAsync(string nodeId, string? filter, bool includeDefaults, CancellationToken ct);
    Task<SetPropertyResult> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct);
    Task<BindingInfoDto?> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct);

    // Events (streaming)
    IAsyncEnumerable<TrackedEventDto> MonitorEventsAsync(string[] eventIds, string? nodeId, CancellationToken ct);
    Task<EventInfoDto[]> GetAvailableEventsAsync(CancellationToken ct);

    // Resources
    Task<ResourceDto[]> GetResourcesAsync(string? nodeId, CancellationToken ct);

    // Diagnostics
    Task<DiagnosticItemDto[]> RunDiagnosticsAsync(string[]? providerNames, CancellationToken ct);
    Task<string[]> GetDiagnosticProvidersAsync(CancellationToken ct);

    // Utility
    Task<string> CaptureScreenshotAsync(string? nodeId, CancellationToken ct);
    Task<string> ExportTreeXmlAsync(string? nodeId, bool recurse, CancellationToken ct);
    Task<TriggerDto[]> GetTriggersAsync(string nodeId, CancellationToken ct);
    Task<BehaviorDto[]> GetBehaviorsAsync(string nodeId, CancellationToken ct);
    Task<WindowDto[]> GetWindowsAsync(CancellationToken ct);
}
```

### Dependency Isolation

The injected agent must not conflict with the target app's own dependencies. Use **ILRepack** to merge StreamJsonRpc.dll, Nerdbank.Streams.dll, and transitive deps into a single assembly with internalized namespaces. The existing `AppDomain.AssemblyResolve` handler in SnoopManager demonstrates this pattern.

---

## MCP Tool Specifications (17 Tools)

### Process Management

| Tool | Description |
|------|-------------|
| `wpf_list_processes` | List all WPF processes with PID, name, title, .NET version, architecture |
| `wpf_attach` | Inject agent into target process, establish session |
| `wpf_detach` | Disconnect from target process |

### Tree Navigation

| Tool | Description |
|------|-------------|
| `wpf_get_visual_tree` | Get visual tree as JSON (depth-limited, supports subtree via rootNodeId) |
| `wpf_find_elements` | Search by type name, x:Name, or property conditions |
| `wpf_get_windows` | List all top-level windows with handles, titles, dimensions |

### Property Inspection

| Tool | Description |
|------|-------------|
| `wpf_get_properties` | Get all properties of an element with values, sources, binding status |
| `wpf_set_property` | Set a property value (TypeConverter coercion) |
| `wpf_get_binding_info` | Detailed binding info: path, source, mode, converter, errors |

### Event Monitoring

| Tool | Description |
|------|-------------|
| `wpf_list_events` | List all registered RoutedEvents |
| `wpf_monitor_events` | Stream routed events in real-time (uses MCP progress notifications) |

### Resources & Diagnostics

| Tool | Description |
|------|-------------|
| `wpf_get_resources` | Inspect resource dictionaries and merged dictionaries |
| `wpf_run_diagnostics` | Run diagnostic providers (binding leaks, non-virtualized lists, etc.) |

### Utility

| Tool | Description |
|------|-------------|
| `wpf_capture_screenshot` | Capture element or window as base64 PNG |
| `wpf_export_tree_xml` | Export tree as XAML-style or structured XML |
| `wpf_get_triggers` | Get triggers on an element (Style, Template, Data triggers) |
| `wpf_get_behaviors` | Get attached behaviors (System.Windows.Interactivity / Microsoft.Xaml.Behaviors) |

Each tool has full JSON input/output schemas defined. See Agent 6's detailed specs for complete schemas.

---

## CLI Command Tree

```
snoop [global options]
  process list [--filter <name>]
  process attach <pid> [--hwnd <handle>]
  process detach <pid>

  tree visual <pid> [--root <nodeId>] [--depth <n>] [--format json|tree]
  tree logical <pid> [--root <nodeId>] [--depth <n>]
  tree find <pid> [--type <name>] [--name <name>]

  property get <pid> <nodeId> [--filter <name>] [--format json|table]
  property set <pid> <nodeId> <property> <value>
  property binding <pid> <nodeId> <property>

  event list <pid>
  event monitor <pid> <event1,event2,...> [--max <n>]

  resource list <pid> [--node <nodeId>]
  diagnostic run <pid> [--providers <p1,p2>] [--min-level warning]

  window list <pid>
  screenshot <pid> [--node <nodeId>] [--output <file.png>]
  export <pid> [--node <nodeId>] [--output <file.xml>]
  trigger list <pid> <nodeId>
  behavior list <pid> <nodeId>
```

Output formats: `json` (default), `jsonl` (streaming), `table` (ASCII via Spectre.Console), `tree` (indented).

---

## Node Addressing

Nodes are addressed by **opaque IDs** generated inside the injected agent as `{dispatcherIndex}_{typeName}_{hashCode:X8}`. The ID is stable for the lifetime of the session.

For human-friendly navigation, the PowerShell provider's `NodePath` convention is also supported:
```
Window\Grid\StackPanel\Button
Window\Grid\StackPanel\Button1   (disambiguated with numeric suffix)
```

Both ID and path formats are accepted in all tool/CLI arguments.

---

## Changes to Existing Code

### Snoop.Core/Infrastructure/SnoopManager.cs (~50 lines changed)

- Add `SnoopStartTarget.HeadlessAgent` enum value
- Add `GetInstanceCreator` case that returns a headless agent bootstrap instead of `SnoopUI`
- Replace 3x `MessageBox.Show()` calls with `TransientSettingsData.MultipleDispatcherMode` policy (already has `AlwaysUse`/`NeverUse` options)
- Replace `ErrorDialog.ShowDialog()` with `Trace.TraceError()` in headless mode

### Snoop.Core/Data/TransientSettingsData.cs (~10 lines added)

- Add `string? PipeName` property for the IPC pipe name
- Add `bool IsHeadless` derived from `StartTarget == HeadlessAgent`

### Snoop.Core/Data/Tree/TreeItem.cs (~5 lines removed)

- Remove `CreateMenuItems()` method (returns WPF `MenuItem[]`)

### Snoop.Core/Infrastructure/PropertyInformation.cs (~20 lines added)

- Add `ToDto()` method that projects all fields to a `PropertyDto` record
- Called inside `Dispatcher.Invoke` before `Teardown()`

### Snoop.Core/Windows/SnoopUI.xaml.cs (no changes)

- Kept for the existing GUI; not used in headless mode

---

## NuGet Dependencies for New Projects

### Snoop.Core.Headless (netstandard2.0)
- None (pure DTOs and interfaces)

### Snoop.InjectedAgent (net462;net6.0-windows)
- `StreamJsonRpc` (ILRepack'd into assembly)
- `Nerdbank.Streams` (ILRepack'd)

### Snoop.MCP (net8.0-windows)
- `ModelContextProtocol` (official C# MCP SDK)
- `Microsoft.Extensions.Hosting`
- `StreamJsonRpc`
- `System.IO.Pipelines`

### Snoop.CLI (net8.0-windows)
- `System.CommandLine` (CLI framework)
- `Spectre.Console` (table/tree output)
- `StreamJsonRpc`

---

## Threading Model

```
MCP/CLI Host Process:
  Thread 1: MCP stdin/stdout handler (or CLI main thread)
  Thread 2: Named pipe listener (per session)

  All tool calls → SessionManager.GetSession(pid) → JsonRpc.InvokeAsync(...)

Injected Agent (inside target WPF process):
  Thread N: Named pipe JSON-RPC server (background)

  All RPC handlers → Dispatcher.InvokeAsync(() => {
      // Call TreeService, PropertyInformation, etc.
      // Project to DTOs
      // Return DTOs (cross thread boundary safely)
  });
```

**Critical:** `PropertyInformation` creates live WPF bindings and MUST run on the Dispatcher. The pattern is: `GetProperties()` → project to DTOs → `Teardown()` — all inside one `Dispatcher.Invoke` call.

---

## Implementation Phases

### Phase 1: Foundation (Snoop.Core.Headless + IPC)
- [ ] Create Snoop.Core.Headless with all DTO records
- [ ] Define ISnoopAgentRpc interface
- [ ] Add `SnoopStartTarget.HeadlessAgent` to SnoopManager
- [ ] Add `PipeName` to TransientSettingsData
- [ ] Remove MessageBox.Show() calls in headless path
- [ ] Build SnoopAgentEntryPoint with named pipe server
- [ ] Test: inject headless agent, retrieve visual tree over pipe

### Phase 2: MCP Server (Snoop.MCP)
- [ ] Create Snoop.MCP project with ModelContextProtocol SDK
- [ ] Implement SessionManager (PID → JsonRpc connection)
- [ ] Implement InjectorBridge (wraps InjectorLauncherManager)
- [ ] Implement all 17 MCP tools
- [ ] Test with Claude Desktop / Claude Code

### Phase 3: CLI (Snoop.CLI)
- [ ] Create Snoop.CLI with System.CommandLine
- [ ] Implement all CLI commands (reuse SessionManager)
- [ ] Add output formatters (JSON, table, tree)
- [ ] Test end-to-end

### Phase 4: Polish
- [ ] Event streaming support (IAsyncEnumerable)
- [ ] Multi-dispatcher support (dispatcher index in node IDs)
- [ ] Error handling and timeout management
- [ ] Build system integration (Nuke targets for new projects)
- [ ] CI/CD pipeline updates
- [ ] Documentation and README

---

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| StreamJsonRpc version conflicts in target process | ILRepack all deps into single assembly |
| PropertyInformation requires Dispatcher thread | All RPC handlers marshal to Dispatcher |
| Large visual trees overwhelm MCP responses | Depth-limited queries, lazy child loading |
| Target process exits during inspection | Session heartbeat, graceful error propagation |
| Elevated target process requires UAC | Existing InjectorLauncher already handles elevation |
| .NET Framework 4.6.2 apps lack modern APIs | Snoop.Core already multi-targets; agent uses same strategy |
| Native GenericInjector DLL is Windows-only | Accept Windows-only constraint (WPF itself is Windows-only) |

---

## What We Keep vs. Discard vs. Create

### Keep As-Is (zero changes)
- `Snoop.InjectorLauncher/` (all 5 files)
- `Snoop.GenericInjector/` (native C++ DLL)
- `TreeService` + all 4 tree service implementations
- `DiagnosticContext` + all 5 diagnostic providers
- `TreeExporter` / `DiagnosticsExporter`
- `EventTracker` + `EventManagerWrapper`
- `PropertyFilter` / `PertinentPropertyFilter`
- `BindingDisplayHelper` / `XamlWriterHelper`
- `VisualCaptureUtil`
- `VisualTreeProvider` (PowerShell — reference for path addressing)
- `NativeMethods` / `WindowHelper` / `AppDomainHelper`
- `TriggerItemBase` hierarchy + `TriggerItemFactory`

### Keep with Minor Changes (~100 lines total)
- `SnoopManager.cs` — add HeadlessAgent path, remove MessageBox
- `TransientSettingsData.cs` — add PipeName field
- `PropertyInformation.cs` — add ToDto() method
- `TreeItem.cs` — remove CreateMenuItems()

### Discard (UI-only, not needed for headless)
- All `.xaml` files in Snoop/ and Snoop.Core/
- `SnoopUI.xaml.cs`, `Zoomer.xaml.cs`, `ScreenshotDialog.xaml.cs`
- All `Views/` UserControls (EventsView, DiagnosticsView, TriggersView, etc.)
- `Controls/` (PropertyGrid2, Previewer, PropertyInspector as UI)
- `Converters/`, `Adorners/`, `ThemeManager`
- `SelectionHighlight/`, `TrackballBehavior`, `BringIntoViewBehavior`
- `WindowFinder.xaml.cs`, `WindowInfoControl.xaml.cs`
- `LowLevelKeyboardHook.cs`, `LowLevelMouseHook.cs`

### Create New
- `Snoop.Core.Headless/` — DTOs + ISnoopAgentRpc (~400 lines)
- `Snoop.InjectedAgent/` — RPC server + engine wrappers (~600 lines)
- `Snoop.MCP/` — 17 MCP tools + SessionManager (~1000 lines)
- `Snoop.CLI/` — CLI commands + formatters (~800 lines)
