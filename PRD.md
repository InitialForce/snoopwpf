# PRD v3: SnoopWPF.Agent — MCP-Enabled WPF Runtime Inspector

> Final revision incorporating two review passes (3x Opus + GPT-5.4-Pro each)

## Overview

SnoopWPF.Agent is a public open-source fork of [SnoopWPF](https://github.com/snoopwpf/snoopwpf) that adds MCP (Model Context Protocol) server and CLI capabilities while preserving the full classic Snoop UI. It enables AI agents — primarily Claude Code / Claude Desktop — to inspect, debug, and interact with running WPF applications programmatically.

**Two integration modes:**

1. **NuGet / Compile-in mode** (primary): A WPF app references the `SnoopWPF.Agent` NuGet package and calls `SnoopAgent.Start()` at startup. The agent runs in-process and exposes an MCP server. No injection, no IPC complexity.

2. **Injection mode** (classic Snoop): An external host process injects the agent into any running WPF process. The same inspection API communicates over named pipes back to the host, which then exposes MCP.

Both modes can run simultaneously with the full Snoop UI window.

**Problem it solves:** WPF debugging today requires a human staring at Snoop's GUI. AI coding agents have no way to inspect a running WPF application's visual tree, diagnose binding errors, check property values, or understand UI state. This project bridges that gap.

## Goals

- Expose Snoop's inspection capabilities as MCP tools consumable by Claude Code
- Provide a NuGet package requiring a single `SnoopAgent.Start()` call to enable MCP inspection
- Maintain the full Snoop UI alongside the MCP agent simultaneously
- Support read operations from day one; write operations opt-in via explicit flag
- Keep best-effort upstream merge compatibility with snoopwpf/snoopwpf
- Achieve open-source quality: documentation, tests, CI, clean architecture
- Design bounded, paginated APIs optimized for AI agent consumption

## Non-Goals (Out of Scope)

- **Cross-platform support** — WPF is Windows-only
- **Non-WPF UI frameworks** — no WinForms, MAUI, Avalonia, or UWP
- **Remote debugging** — MCP server binds to `127.0.0.1` only; no configurable hostname
- **Replacing Snoop's UI** — the GUI is preserved intact
- **Method invocation** — `wpf_invoke_method` deferred to future version (security spike needed first)
- **Session reattach** — if the host crashes, a new injection is required (v1 simplicity)
- **Automated UI testing** — this is an inspection/debugging tool, not a test runner
- **NuGet mode for .NET 6/7** — NuGet package requires .NET 8+. Older WPF apps use injection mode.

## Quality Gates (Per Milestone)

**Milestone 1 (Foundation):**
- `dotnet build Snoop.sln` — full solution builds
- `dotnet test Snoop.Core.Tests` — existing tests pass
- `dotnet test SnoopWPF.Agent.Tests` — new unit tests pass

**Milestone 2 (MVP Tools):**
- All Milestone 1 gates
- `dotnet test SnoopWPF.Agent.IntegrationTests` — integration tests pass
- MCP conformance: `tools/list` returns valid schemas, tool calls return valid JSON

**Milestone 3 (Full NuGet):** All Milestone 2 gates + all tools pass integration tests

**Milestone 4 (Injection):** All Milestone 3 gates + injection-mode integration tests pass

**Milestone 5 (Release):** All gates + NuGet package + CI pipeline green

---

## Architecture

### Project Structure

```
Snoop.sln
│
│── Snoop.Core/                          EXISTING — inspection engine
│     TargetFrameworks: net462;net6.0-windows
│     Minor additive changes only
│
│── Snoop.Injector/                      NEW — shared injection orchestration
│     TargetFrameworks: net462;net6.0-windows;net8.0-windows
│     Extracted from Snoop/: InjectorLauncherManager, ProcessInfo (attach logic)
│     Note: ProcessInfo.Snoop() uses string constants for assembly/class/method
│           names (no typeof(SnoopManager) reference) to avoid depending on Snoop.Core
│     Referenced by: Snoop/, SnoopWPF.Agent.Host, SnoopWPF.Agent.CLI
│
│── SnoopWPF.Agent.Contracts/            NEW — shared protocol contracts
│     TargetFramework: netstandard2.0
│     Contents: DTOs (mutable POCO classes with parameterless constructors and
│               settable properties — NO records, NO serializer attributes),
│               ISnoopInspector interface (Task-based, CancellationToken on every
│               method, NO IAsyncEnumerable), error codes, pagination types,
│               transport envelope types (PipeRequest, PipeResponse, PipeCancel,
│               HandshakeMessage), protocol constants
│     Dependencies: NONE
│
│── SnoopWPF.Agent.Engine/               NEW — headless inspection engine wrapper
│     TargetFrameworks: net462;net6.0-windows;net8.0-windows  (UseWpf=true)
│     Contents: SnoopInspector (implements ISnoopInspector), wraps TreeService,
│               PropertyInformation, DiagnosticContext, etc.
│               All calls marshaled to WPF Dispatcher. DTO projection.
│     Dependencies: Snoop.Core, SnoopWPF.Agent.Contracts
│
│── SnoopWPF.Agent.Tools/                NEW — shared MCP tool definitions
│     TargetFramework: net8.0-windows
│     Contents: All [McpServerToolType] classes operating against ISnoopInspector.
│               Used by both NuGet-mode server and injection-mode host.
│     Dependencies: SnoopWPF.Agent.Contracts, ModelContextProtocol
│
│── SnoopWPF.Agent/                      NEW — NuGet package + MCP server
│     TargetFramework: net8.0-windows
│     Contents: SnoopAgent public entry point, HTTP/SSE server setup,
│               stdio transport support
│     Dependencies: SnoopWPF.Agent.Engine, SnoopWPF.Agent.Tools,
│                   SnoopWPF.Agent.Contracts, ModelContextProtocol.AspNetCore
│     Output: Class library (NuGet package)
│
│── SnoopWPF.Agent.Remote/               NEW — named-pipe client proxy
│     TargetFramework: net8.0-windows
│     Contents: PipeSnoopInspectorProxy (ISnoopInspector over named pipe),
│               pipe client, framed JSON transport, error mapping
│     Dependencies: SnoopWPF.Agent.Contracts
│
│── SnoopWPF.Agent.Injection/            NEW — injected agent (runs inside target)
│     TargetFrameworks: net462;net6.0-windows  (UseWpf=true)
│     Contents: Entry point for injection, named-pipe client (connects to host's server pipe),
│               custom framed JSON protocol
│     Dependencies: SnoopWPF.Agent.Engine, SnoopWPF.Agent.Contracts
│     NOTE: ZERO external NuGet dependencies beyond BCL.
│           Exception: net462 may reference System.IO.Pipes.AccessControl
│           for pipe ACLs (see US-023).
│
│── SnoopWPF.Agent.Host/                 NEW — injection-mode MCP host
│     TargetFramework: net8.0-windows
│     Contents: Injects agent, connects via pipe, exposes MCP
│     Dependencies: Snoop.Injector, SnoopWPF.Agent.Remote,
│                   SnoopWPF.Agent.Tools, SnoopWPF.Agent.Contracts,
│                   ModelContextProtocol, ModelContextProtocol.AspNetCore
│     Output: Executable (snoop-mcp.exe)
│
│── SnoopWPF.Agent.CLI/                  NEW — CLI tool
│     TargetFramework: net8.0-windows
│     Dependencies: Snoop.Injector, SnoopWPF.Agent.Remote,
│                   SnoopWPF.Agent.Contracts, System.CommandLine, Spectre.Console
│     Output: Executable (snoop-cli.exe)
│
│── SnoopWPF.Agent.Tests/               NEW — unit tests
│── SnoopWPF.Agent.IntegrationTests/    NEW — integration tests
│── SnoopWPF.Agent.SampleApp/           NEW — sample WPF app
│
│── Snoop/                               EXISTING (references Snoop.Injector now)
│── Snoop.Console/                       EXISTING (UNCHANGED)
│── Snoop.InjectorLauncher/              EXISTING (UNCHANGED)
│── Snoop.GenericInjector/               EXISTING (UNCHANGED)
│── Snoop.Core.Tests/                    EXISTING (UNCHANGED)
```

### Dependency Graph

```
SnoopWPF.Agent (NuGet, net8.0-windows)
  └─> SnoopWPF.Agent.Engine (net462;net6.0-windows;net8.0-windows)
  │     └─> Snoop.Core (existing)
  │     └─> SnoopWPF.Agent.Contracts (netstandard2.0)
  └─> SnoopWPF.Agent.Tools (net8.0-windows)
  │     └─> SnoopWPF.Agent.Contracts
  │     └─> ModelContextProtocol
  └─> ModelContextProtocol.AspNetCore

SnoopWPF.Agent.Host (snoop-mcp.exe, net8.0-windows)
  └─> SnoopWPF.Agent.Remote (pipe client proxy)
  │     └─> SnoopWPF.Agent.Contracts
  └─> SnoopWPF.Agent.Tools (shared tool handlers!)
  └─> Snoop.Injector (net462;net6.0-windows;net8.0-windows)
  └─> ModelContextProtocol, ModelContextProtocol.AspNetCore

SnoopWPF.Agent.CLI (snoop-cli.exe, net8.0-windows)
  └─> SnoopWPF.Agent.Remote
  └─> Snoop.Injector
  └─> SnoopWPF.Agent.Contracts

SnoopWPF.Agent.Injection (injected DLL, net462;net6.0-windows)
  └─> SnoopWPF.Agent.Engine
  └─> SnoopWPF.Agent.Contracts
  └─> ZERO external NuGet deps (exception: System.IO.Pipes.AccessControl on net462)

Snoop/ (existing GUI, net462)
  └─> Snoop.Injector (consumes net462 TFM)
  └─> Snoop.Core
```

**Key design rules:**
- `SnoopWPF.Agent.Injection` has zero external NuGet deps (one narrow exception for pipe ACLs on net462).
- `SnoopWPF.Agent.Contracts` has zero deps, no serializer attributes. JSON policy in transport layers.
- `SnoopWPF.Agent.Tools` is the shared MCP tool handler layer. Both `SnoopWPF.Agent` (NuGet) and `SnoopWPF.Agent.Host` (injection) reference it. Tool handlers operate against `ISnoopInspector` — in NuGet mode that's `SnoopInspector` directly; in injection mode that's `PipeSnoopInspectorProxy`.
- `Snoop.Injector` multi-targets `net462;net6.0-windows;net8.0-windows` so the existing net462 Snoop GUI can reference it.

### Transport Strategy

**Primary: stdio** — Claude Code launches snoop-mcp as subprocess. Zero configuration.

```json
{
  "mcpServers": {
    "snoop": { "command": "snoop-mcp", "args": ["--pid", "12345"] }
  }
}
```

**Secondary: HTTP/SSE** — For NuGet/compile-in mode where the server runs inside the app.

- Bearer token required on all HTTP requests (`Authorization: Bearer {token}`)
- Token delivery: `SnoopAgentHandle.BearerToken` property + JSON discovery file (`%TEMP%\snoop-agent-{pid}.json`) with owner-only ACL
- CORS: deny all origins explicitly
- Token must NEVER appear in URL query strings (header-only enforcement)
- Configurable pre-shared key via `SnoopAgentOptions.BearerToken` for stable dev configs. If null, random token generated per session.

```json
// Discovery file written by SnoopAgent.Start() — ACL'd owner-only
{
  "endpoint": "http://127.0.0.1:5432/mcp",
  "bearerToken": "a1b2c3d4...",
  "pid": 12345,
  "processName": "MyApp"
}
```

**Phase 0 spike required** to verify Claude Code connects to both transports before committing. All Milestone 2+ stories use whichever transport the spike validates. If HTTP fails, NuGet mode falls back to a stdio bridge subprocess.

### Threading Model

All WPF object access via Dispatcher:

1. MCP request arrives on transport thread
2. `SnoopInspector` marshals to Dispatcher via `Dispatcher.InvokeAsync(DispatcherPriority.Send)`
3. On Dispatcher: call engine, project to DTOs, call `Teardown()` — all in one synchronous invoke
4. Return DTOs (plain data, safe to cross threads)

**Per-dispatcher exclusive work queue** serializes operations. No concurrent Dispatcher access.

**Timeout:** configurable per-operation (default 5s). Returns `DispatcherBusy` error.

**Multi-dispatcher:** Supported from day one in the protocol (node IDs encode dispatcher index). Implementation of multi-dispatcher work queues is a separate optional story (US-003e) — single-dispatcher covers 95%+ of WPF apps. `wpf_get_session_info` returns dispatcher list; node IDs encode dispatcher via `{dispatcherIdx}:{counter}`.

### Node Identity

Monotonically incrementing integer per session: `{dispatcherIdx}:{counter}` (e.g., `0:42`).

**Two data structures:**
- `ConditionalWeakTable<object, NodeRegistration>` — forward lookup (`object → nodeId`). GC-friendly.
- `Dictionary<int, WeakReference<object>>` — reverse lookup (`nodeId → object`). Entries whose `WeakReference.TryGetTarget()` returns false are pruned lazily on each access. For long sessions, a periodic sweep runs every 60s.

`TreeItem` instances are NOT cached across RPC calls — constructed on-demand, projected to DTOs, discarded.

Path aliases also accepted: `Window\Grid\StackPanel\Button`.

### Cursor Pagination Design

Cursors encode a **snapshot of child node IDs** taken at first-page time, not a bare offset. This prevents gaps/duplicates when the visual tree mutates between pages.

```
Page 1 request: { nodeId: "0:5", take: 50 }
→ Engine snapshots all child nodeIds: [0:10, 0:11, ..., 0:109] (100 total)
→ Returns items 0-49, cursor encodes "snapshot-id + offset 50"
→ Snapshot cached with 30s TTL

Page 2 request: { nodeId: "0:5", cursor: "snap_abc_50", take: 50 }
→ Engine retrieves cached snapshot, returns items 50-99
→ If snapshot expired (>30s), returns stale: true with a fresh first page
```

For `wpf_get_properties`: properties sorted by name before pagination (stable ordering contract).

All paginated responses include: `items`, `nextCursor?`, `totalCount`, `hasMore`, `stale` (boolean, true if snapshot expired and result was regenerated).

### SnoopManager Integration

`SnoopManager.GetInstanceCreator()` returns `Func<SnoopMainBaseWindow>`. The headless path cannot return a Window.

**Fix (US-003d):**
1. New `IInjectedAgent` interface: `Start(TransientSettingsData)`, `Stop()`
2. New `InjectAgentIntoDispatchers` method (forked from existing) works with `IInjectedAgent`
3. `SnoopManager.HeadlessAgentFactory` static property for factory registration
4. `MessageBox.Show()` skipped in headless mode — `MultipleDispatcherMode` defaults to `AlwaysUse`
5. `ErrorDialog.ShowDialog()` → `Trace.TraceError()` in headless path
6. Existing GUI path completely untouched

This is ~200-300 lines of changes to SnoopManager.

### Pipe Protocol (Injection Mode)

**Roles:** Host creates `NamedPipeServerStream`. Injected agent connects via `NamedPipeClientStream`.

**Framing:** `{4-byte LE length}{UTF-8 JSON payload}`. Max frame: 10MB.

**Envelopes:**
```
Request:  {"id": 1, "method": "GetChildren", "params": {...}}
Response: {"id": 1, "result": {...}}
Error:    {"id": 1, "error": {"code": "NODE_NOT_FOUND", "message": "...", "suggestion": "..."}}
Cancel:   {"id": 1, "cancel": true}   // host → agent: cancel in-flight request
```

**Handshake sequence:**
1. Host creates pipe, waits for connection
2. Agent connects as client
3. Host sends `{"sessionToken": "...", "protocolVersion": 1}` (host speaks first to challenge)
4. Agent validates token, responds with capabilities:
```json
{
  "protocolVersion": 1,
  "agentVersion": "1.0.0",
  "targetRuntime": "net8.0-windows",
  "sessionToken": "a1b2c3d4-...",
  "dispatchers": [{ "id": 0, "threadId": 1234 }],
  "capabilities": ["tree", "properties", "diagnostics", "resources", "screenshots"]
}
```

Host validates `sessionToken` matches the one from `TransientSettingsData`. Rejects mismatched protocol versions. Verifies client PID via `GetNamedPipeClientProcessId()` matches injected process.

**Pipe security:**
- Name: `SnoopAgent_{randomGUID}` (unpredictable)
- ACL: `PipeOptions.CurrentUserOnly` on net6+; `PipeSecurity`/`PipeAccessRule` on net462
- Connection deadline: 10 seconds after injection

---

## Security & Privacy Model

### Core Principles

1. **Mutations disabled by default.** `EnableMutation` defaults to `false`. Mutation tools return `MutationDisabled` error.
2. **Sensitive property redaction (keyword match).** Properties whose name contains any of: `password`, `passwd`, `pwd`, `secret`, `apikey`, `connectionstring`, `connstr`, `credential`, `privatekey`, `sharedkey`, `cookie`, `sessionkey`, `authorization`, `authtoken`, `authkey`, `accesstoken`, `bearertoken`, `refreshtoken`, `sessiontoken`, `sastoken`, `jwttoken` (case-insensitive substring match). Note: bare `auth` and `token` are NOT keywords (too broad — would redact `IsAuthorized`, `CancellationToken`, etc.). All `SecureString`-typed properties. `PasswordBox.Password` always redacted. **Property getter is NOT invoked** for redacted properties — return `[REDACTED]` without calling the getter.
3. **Localhost only.** Hardcoded `127.0.0.1`. No configurable hostname parameter.
4. **Bearer token on HTTP.** Required on all requests via `Authorization: Bearer {token}` header. Query-string tokens rejected. CORS denies all origins. Token delivered via `SnoopAgentHandle.BearerToken` property + JSON discovery file with owner-only ACL.
5. **Configurable pre-shared key.** `SnoopAgentOptions.BearerToken` allows stable token for development (avoids rotation on every restart). If null, random token generated.
6. **Pipe security.** Random GUID names, current-user ACLs, session token handshake, client PID verification via `GetNamedPipeClientProcessId()`.
7. **Minimal process handle lifetime.** `PROCESS_ALL_ACCESS` handle closed immediately after injection.
8. **Log sanitization.** Pipe names, session tokens, settings file paths, and property values are never logged. Exception payloads sanitized before logging. `SnoopLog.txt` ACL'd to owner, rotated per session.
9. **Settings file security.** `TransientSettingsData` temp file written with owner-only DACL. Deleted by injected agent immediately after reading session token. Token zeroed from memory after handshake.

### TypeConverter Safety (for wpf_set_property)

Settable types resolved from **hardcoded internal converter table only**. `TypeDescriptor.GetConverter()` is NEVER called for mutation operations — this prevents app-registered custom converters from executing.

Safe types: `string`, `bool`, `int`, `double`, `float`, `decimal`, `long`, `Color`, `Thickness`, `GridLength`, `CornerRadius`, `FontWeight`, `FontStyle`, `Visibility`, `HorizontalAlignment`, `VerticalAlignment`, `TextAlignment`, `Point`, `Size`, `Rect`, all enum types.

Never settable: `Uri`, `ImageSource`, `BitmapSource`, `FontFamily`, `Style`, `ControlTemplate`, `DataTemplate`, `Binding`, `Type`, any `UIElement` subtype.

### Property Getter Side Effects

Reading a WPF property invokes its getter in the target process. Getters can trigger lazy-loading, network requests, or state changes. `SECURITY.md` must document: "Reading properties causes WPF property getter execution in the target process. This can have side effects beyond observation."

### Threat Mitigations

| Threat | Mitigation |
|--------|-----------|
| Remote access | `127.0.0.1` only, hardcoded |
| Browser JS (CORS/SSE) | Bearer token required, CORS deny all, query-string tokens rejected |
| Password/PII in properties | Contains-match redaction, getter skipped for sensitive props |
| Property mutation | Disabled by default; opt-in; hardcoded converter table |
| Pipe hijacking | Random GUID name, user ACL, session token, client PID verification |
| Settings file exposure | Owner-only DACL, deleted after read, token zeroed from memory |
| TypeConverter gadgets | Hardcoded converter table, never TypeDescriptor.GetConverter() |
| Log leakage | Pipe names/tokens/property values never logged; exception sanitization |
| Target app crash | All Dispatcher calls wrapped in try/catch; never propagate |

---

## Performance Targets

| Metric | Target |
|--------|--------|
| `GetChildren` (100 children) | < 50ms |
| `GetProperties` (typical element, ~80 props) | < 100ms |
| `RunDiagnostics` (full tree, 1000 elements) | < 2s |
| `CaptureScreenshot` | < 500ms |
| MCP server startup | < 200ms |
| Memory overhead (idle) | < 20MB |

---

## MCP Tools (15 tools)

### API Design Principles

1. **Bounded responses** — cursor pagination with hard caps (`take <= 200`, `maxResults <= 100`)
2. **Summary + detail** — `GetChildren` returns summaries; `GetProperties` is separate
3. **Stable opaque IDs** — monotonic counter in ConditionalWeakTable
4. **Cursor pagination** — snapshot-based cursors with `stale` flag for dynamic trees
5. **Structured errors** — `SnoopErrorCode` + message + `suggestion` for AI recovery
6. **MCP image content type** — screenshots as `ImageContent` blocks (no temp files)
7. **Explicit truncation** — `totalCount`, `hasMore`, `stale`, `childrenTruncated` on tree nodes
8. **Properties sorted by name** for stable pagination ordering

### Tool Inventory

| # | Tool | Description |
|---|------|-------------|
| 1 | `wpf_get_session_info` | Process name, PID, .NET version, dispatchers (with window nodeIds), capabilities, mutationEnabled |
| 2 | `wpf_get_windows` | List top-level windows (nodeId, title, type, dimensions, dispatcherId) |
| 3 | `wpf_get_visual_tree` | Depth-limited tree dump. Nodes include `childrenTruncated: true` where cut. Max 5000 nodes. |
| 4 | `wpf_get_children` | Cursor-paginated direct children. Snapshot-based cursors. |
| 5 | `wpf_get_ancestors` | Ancestor chain from node to root (each with typeName, name, dataContextType) |
| 6 | `wpf_find_elements` | Search by type, name, or property value. Subtree-scoped via rootNodeId. |
| 7 | `wpf_inspect_element` | Rich element summary (path, parent, dimensions, DataContext, error counts) |
| 8 | `wpf_get_properties` | Cursor-paginated properties with values, sources, binding status, isReadOnly, isRedacted |
| 9 | `wpf_set_property` | Set property value (opt-in, hardcoded converter table, culture-invariant) |
| 10 | `wpf_get_binding_info` | Binding debugging: path, mode, status, error, dataContextIsNull, dataContextType, resolvedValue |
| 11 | `wpf_run_diagnostics` | Diagnostic providers with subtree scope and cursor pagination |
| 12 | `wpf_get_resources` | Resource dictionary with precedence (effective first, shadowed included) |
| 13 | `wpf_capture_screenshot` | Element/window screenshot as MCP ImageContent (multi-block response) |
| 14 | `wpf_get_triggers` | Triggers on element (Style/Template/Element) |
| 15 | `wpf_get_behaviors` | Attached behaviors (both Interactivity and Microsoft.Xaml.Behaviors) |

**Deferred to v2:** `wpf_monitor_events` (bounded event capture — temporal debugging), `wpf_invoke_method` (security spike needed), `wpf_export_tree_xml` (large payloads, `wpf_get_visual_tree` covers use case).

### Key Tool Schemas

#### wpf_get_session_info
```
Input: {} 
Output: { processName, pid, dotnetVersion, mutationEnabled,
          dispatchers: [{ id, threadId, windowNodeIds: string[] }],
          capabilities: string[] }
```

#### wpf_get_visual_tree
```
Input: { rootNodeId?: string, maxDepth?: number (default 3, max 10),
         treeType?: "visual"|"logical"|"automation",
         includeProperties?: string[] (DependencyProperty names, case-insensitive, max 10 entries).
                             Redacted properties return "[REDACTED]" inline. }
Output: { root: NodeDto, truncated: boolean, returnedNodeCount: number }
NodeDto: { nodeId, typeName, name, displayName, childCount, hasBindingError,
           depth, childrenTruncated: boolean, properties?: {}, children?: NodeDto[] }
Hard cap: 5000 nodes. Nodes at cut boundary have childrenTruncated: true.
```

#### wpf_get_children
```
Input: { nodeId?: string (omit for app roots), treeType?: "visual"|"logical"|"automation",
         cursor?: string, take?: number (default 50, max 200) }
Output: { items: NodeDto[], nextCursor?: string, totalCount, hasMore, stale }
Root ordering when nodeId omitted: main window first, then by title.
```

#### wpf_get_ancestors
```
Input: { nodeId: string, maxLevels?: number }
Output: { ancestors: [{ nodeId, typeName, name, dataContextType }] }
Ordered: immediate parent first, root last.
```

#### wpf_find_elements
```
Input: { typeName?, name?, rootNodeId?: string (subtree scope),
         propertyConditions?: [{ property, operator: "Equals"|"Contains", value }],
         treeType?, maxResults? (default 20, max 100) }
Output: { results: [{ node: NodeDto, path: string[] }],
          totalScanned, truncated: boolean }
typeName matches short ("Button") or full name. Case-insensitive.
```

#### wpf_inspect_element
```
Input: { nodeId }
Output: { nodeId, typeName, name, displayName, path: string[], parentNodeId,
          childCount, depth, dispatcherId, isVisible, actualWidth, actualHeight,
          dataContextType, hasBindingErrors, bindingErrorCount,
          triggerCount: number|null, behaviorCount: number|null }
null for triggerCount/behaviorCount means "not evaluated" (call dedicated tool).
```

#### wpf_get_properties
```
Input: { nodeId, filter? (case-insensitive substring match on property name),
         category?: "layout"|"color"|"font"|"grid"|"all",
         includeDefaults? (default false), cursor?, take? (default 100, max 200) }
Output: { items: PropertyDto[], nextCursor?, totalCount, hasMore, stale }
PropertyDto: { name, typeName, value, valueSource, isLocallySet, isDataBound,
               hasBindingError, bindingError?, isReadOnly, hasTypeConverter, isRedacted }
Properties sorted by name (stable ordering for pagination).
```

#### wpf_set_property
```
Input: { nodeId, propertyName, value: string }
Output: { success, previousValue, newValue, error? }
Errors: MutationDisabled, PropertyReadOnly, UnsupportedPropertyType,
        TypeConversionFailed (includes expectedFormat hint), PropertyRedacted
Conversion: InvariantCulture. Hardcoded converter table.
Value format hints: Color="#RRGGBB" or "Red"; Thickness="L,T,R,B" or single; 
  GridLength="Auto"|"*"|"2*"|"100"; Visibility="Visible"|"Hidden"|"Collapsed"
```

#### wpf_get_binding_info
```
Input: { nodeId, propertyName }
Output: { hasBinding, bindingType, path, elementName, relativeSource, mode,
          updateSourceTrigger, converterTypeName, sourceType,
          status: "Active"|"PathError"|"UpdateTargetError"|...,
          error?, dataContextIsNull, dataContextType, resolvedValue?,
          childBindings?: [] }
```

#### wpf_run_diagnostics
```
Input: { nodeId?: string (subtree scope), providers?, minLevel?,
         cursor?, take? (default 50, max 200) }
Output: { items: DiagnosticItemDto[], nextCursor?, totalCount, hasMore }
DiagnosticItemDto: { name, description, area, level, nodeId, nodePath: string[] }
Sorted by level (Critical first).
```

#### wpf_get_resources
```
Input: { nodeId?, resourceKey?, cursor?, take? (default 50, max 200) }
Output: { items: ResourceDto[], nextCursor?, totalCount, hasMore }
ResourceDto: { key, valueTypeName, valueSummary, origin, dictionarySource }
Precedence: effective (closest scope) first. Shadowed resources included with their origin.
```

#### wpf_capture_screenshot
```
Input: { nodeId?: string }
Output: Multi-block MCP response:
  [0] TextContent: { "width": 800, "height": 600, "nodeId": "0:5" }
  [1] ImageContent: { data: "<base64 PNG>", mimeType: "image/png" }
Fallback order: specified node → MainWindow → first visible window.
No temp files. No filePath in response.
Errors: NodeNotFound, zero-size element, hidden window.
```

#### wpf_get_triggers
```
Input: { nodeId }
Output: [{ triggerType, isActive, source: "Style"|"Template"|"Element",
           conditions: [{ property, value }], setters: [{ property, value }] }]
```

#### wpf_get_behaviors
```
Input: { nodeId }
Output: [{ typeName, assemblyName, properties: { name: value } }]
Works with both System.Windows.Interactivity and Microsoft.Xaml.Behaviors.
```

### Error Response Schema

```json
{
  "code": "NODE_NOT_FOUND",
  "message": "Element with ID 0:42 no longer exists",
  "suggestion": "Re-navigate from wpf_get_windows — element was likely garbage collected",
  "detail": { "nodeId": "0:42" }
}
```

| Error Code | Suggestion |
|-----------|-----------|
| `NODE_NOT_FOUND` | "Re-navigate from wpf_get_windows — element was likely garbage collected" |
| `DISPATCHER_BUSY` | "Retry the call; if repeated, the WPF app may be performing a long UI operation" |
| `OPERATION_TIMED_OUT` | "Reduce scope (smaller subtree, fewer properties) or retry when app is idle" |
| `PROPERTY_READ_ONLY` | "This property cannot be set; use wpf_get_properties to find writable properties" |
| `TYPE_CONVERSION_FAILED` | "Check value format; Color=#RRGGBB or named; Thickness=L,T,R,B; see tool description" |
| `UNSUPPORTED_PROPERTY_TYPE` | "Only primitive and common WPF value types are settable; see tool description for list" |
| `MUTATION_DISABLED` | "Mutations disabled; set EnableMutation=true in SnoopAgentOptions to allow changes" |
| `PROPERTY_REDACTED` | "This property is redacted for security; its value cannot be read or set" |
| `SESSION_NOT_FOUND` | "No active session; the target process may have exited" |
| `PROTOCOL_MISMATCH` | "Agent and host protocol versions differ; update to matching versions" |
| `ELEMENT_NOT_RENDERABLE` | "Element has zero size or is not visible; try wpf_get_windows for a full window screenshot instead" |

---

## User Stories

### Phase 0: Spikes

#### US-000a: Transport Spike
**Acceptance Criteria:**
- [ ] Minimal MCP server (one dummy tool) with stdio transport — Claude Code connects and calls tool
- [ ] Same with HTTP/SSE transport — Claude Code/Desktop connects with bearer token in `Authorization` header
- [ ] Test MCP `ImageContent` block — verify Claude can render an inline image from a tool response
- [ ] Pin `ModelContextProtocol` NuGet version (latest stable 1.x)
- [ ] Decision recorded: which transport for NuGet mode, which for injection mode

#### US-000b: In-Process Engine Spike
**Acceptance Criteria:**
- [ ] Minimal .NET 8 WPF app with a Button
- [ ] `SnoopInspector` instantiated with `Application.Current.Dispatcher`
- [ ] Call `TreeService.Construct(Application.Current, parent: null)` and verify children returned
- [ ] Project to `NodeDto`, verify fields populated
- [ ] No deadlocks, no crashes

#### US-000c: Headless CI Spike
**Acceptance Criteria:**
- [ ] WPF app launches on GitHub Actions `windows-latest`
- [ ] Tree traversal and property inspection work without display
- [ ] Screenshot capture tested — document results
- [ ] Test both net6.0 and net8.0 target apps

#### US-000d: Injection Transport Spike
**Acceptance Criteria:**
- [ ] net8.0 host creates named pipe with `PipeOptions.CurrentUserOnly`
- [ ] net6.0 "injected" app connects, handshake succeeds, request/response round-trip works
- [ ] **Also test net462 path:** pipe creation with `PipeSecurity`/`PipeAccessRule` (no `PipeOptions.CurrentUserOnly`)
- [ ] Cancel frame works
- [ ] Length-prefix framing with max 10MB enforced

---

### Milestone 1: Foundation

#### US-001: SnoopWPF.Agent.Contracts

**Acceptance Criteria:**
- [ ] `netstandard2.0`, zero NuGet deps
- [ ] **Mutable POCO classes** with parameterless constructors and settable properties (NOT records — needed for `DataContractJsonSerializer` on net462)
- [ ] DTOs: `NodeDto`, `PropertyDto`, `BindingInfoDto`, `DiagnosticItemDto`, `ResourceDto`, `TriggerDto`, `BehaviorDto`, `WindowDto`, `SetPropertyResultDto`, `ScreenshotMetadataDto`
- [ ] `PropertyDto` includes: `isReadOnly`, `hasTypeConverter`, `isRedacted`
- [ ] `ISnoopInspector` interface: all methods Task-based, CancellationToken on every method, NO `IAsyncEnumerable`
- [ ] `SnoopErrorCode` enum with all 10 codes
- [ ] `SnoopException` with code + message + suggestion
- [ ] `CursorPage<T>`: items, nextCursor, totalCount, hasMore, stale, truncated
- [ ] Transport types: `PipeRequest` (id, method, params), `PipeResponse` (id, result/error), `PipeCancel` (id), `HandshakeMessage` (protocolVersion, agentVersion, targetRuntime, sessionToken, dispatchers, capabilities)
- [ ] Protocol constants: `MaxFrameSize = 10MB`, `ProtocolVersion = 1`
- [ ] No serializer-specific attributes anywhere

#### US-002: Snoop.Injector — extract injection orchestration

**Acceptance Criteria:**
- [ ] `Snoop.Injector.csproj` targeting `net462;net6.0-windows;net8.0-windows`
- [ ] `InjectorLauncherManager` moved from `Snoop/`
- [ ] `ProcessInfo` attach logic moved; uses string constants for assembly/class/method names (NOT `typeof(SnoopManager)`) to avoid Snoop.Core dependency
- [ ] `WindowInfo` and WPF process discovery moved
- [ ] `Snoop/` references `Snoop.Injector` instead
- [ ] All 3 `Snoop.InjectorLauncher.{arch}.exe` binaries included as content
- [ ] Existing Snoop GUI works identically (verified by building and running)
- [ ] All existing Snoop.Core.Tests pass

#### US-003: Engine — tree and property inspection

**Acceptance Criteria:**
- [ ] `SnoopWPF.Agent.Engine.csproj` targeting `net462;net6.0-windows;net8.0-windows` (UseWpf=true)
- [ ] `SnoopInspector` implementing tree + property methods from `ISnoopInspector`
- [ ] Constructor accepts `Dispatcher` and optional root `Visual`/`Application`
- [ ] All methods marshal via `Dispatcher.InvokeAsync(DispatcherPriority.Send)`
- [ ] PropertyInformation: construct → read values → project to `PropertyDto[]` → `Teardown()` all in ONE Dispatcher invoke. Existing `isRunning` flag + `Teardown()` pattern sufficient (no additional guard needed).
- [ ] Tree traversal via existing `TreeService` (Visual, Logical, Automation)
- [ ] **Node registry:** `ConditionalWeakTable<object, NodeRegistration>` (forward) + `Dictionary<int, WeakReference<object>>` (reverse, lazy-pruned on access + 60s sweep)
- [ ] **Cursor pagination:** snapshot-based cursors (child nodeId snapshot cached with 30s TTL). `stale` flag when snapshot expired.
- [ ] Properties sorted by name for stable cursor pagination
- [ ] Configurable timeout (default 5s), `DispatcherBusy` error on timeout
- [ ] **Redaction:** contains-match on keyword list. Property getter NOT invoked for redacted properties.
- [ ] Single-dispatcher only in this story (dispatcherIdx always 0)
- [ ] Unit tests: projection, pagination, timeout, redaction, reverse lookup pruning

#### US-003b: Engine — diagnostics, resources, screenshots

**Acceptance Criteria:**
- [ ] Diagnostics delegates to `DiagnosticContext` with all 5 providers
- [ ] Subtree-scoped diagnostics via root nodeId
- [ ] Diagnostics support cursor pagination (`CursorPage<DiagnosticItemDto>`)
- [ ] Resource inspection: walk up tree, collect with origin labels
- [ ] Precedence: effective (closest) first, shadowed included
- [ ] Screenshot: `VisualCaptureUtil.SaveVisual()` → byte[] PNG in memory (NOT temp file)
- [ ] Unit tests

#### US-003c: Engine — triggers and behaviors

**Acceptance Criteria:**
- [ ] `TriggerInspector.GetTriggers(DependencyObject)` — constructs TriggerItems on Dispatcher, projects to `TriggerDto[]`, disposes
- [ ] `BehaviorInspector.GetBehaviors(DependencyObject)` — reflection-based, both Interactivity + Microsoft.Xaml.Behaviors
- [ ] Both handle elements with no triggers/behaviors gracefully
- [ ] Unit tests

#### US-003d: SnoopManager headless agent support

**Acceptance Criteria:**
- [ ] `IInjectedAgent` interface: `Start(TransientSettingsData)`, `Stop()`
- [ ] New `InjectAgentIntoDispatchers` method works with `IInjectedAgent`
- [ ] `SnoopStartTarget.HeadlessAgent` enum value
- [ ] `TransientSettingsData` gains: `PipeName`, `SessionToken` (both handled correctly with `XmlSerializer`)
- [ ] `SnoopManager.HeadlessAgentFactory` static property
- [ ] `MessageBox.Show()` skipped in headless; `MultipleDispatcherMode` → `AlwaysUse`
- [ ] `ErrorDialog.ShowDialog()` → `Trace.TraceError()` in headless
- [ ] **Settings file security:** written with owner-only DACL; deleted by agent after reading; token zeroed from memory after handshake
- [ ] **Log sanitization:** InjectorLauncher must not log TransientSettingsData path containing session token
- [ ] Existing GUI completely unaffected; all Snoop.Core.Tests pass

#### US-003e: Engine — multi-dispatcher support (optional for MVP)

**Description:** Support WPF apps with multiple dispatchers. Only needed if target app actually has multiple dispatchers (rare).

**Acceptance Criteria:**
- [ ] Per-dispatcher `SnoopInspector` instances with independent work queues
- [ ] Node IDs encode dispatcher index: `{dispatcherIdx}:{counter}`
- [ ] `wpf_get_session_info` returns dispatcher list with window nodeIds per dispatcher
- [ ] All tools route to correct dispatcher based on nodeId prefix
- [ ] Can be deferred without blocking MVP — single-dispatcher (dispatcherIdx=0) works for 95%+ of apps

---

### Milestone 2: MVP Tools (NuGet Mode)

#### US-004: SnoopWPF.Agent.Tools — shared MCP tool handlers

**Acceptance Criteria:**
- [ ] `SnoopWPF.Agent.Tools.csproj` targeting `net8.0-windows`
- [ ] All 15 `[McpServerToolType]` classes operating against `ISnoopInspector`
- [ ] Tool schemas with full input/output JSON Schema annotations
- [ ] Error mapping: `SnoopException` → MCP error response with code + suggestion
- [ ] Unit tests: each tool handler with mock `ISnoopInspector`

#### US-005: SnoopWPF.Agent — NuGet MCP server package

**Acceptance Criteria:**
- [ ] `SnoopWPF.Agent.csproj` targeting `net8.0-windows`
- [ ] Public API: `SnoopAgent.Start(Application, SnoopAgentOptions?)` → `SnoopAgentHandle`
- [ ] `SnoopAgentOptions`: `Port` (default auto), `EnableMutation` (default false), `EnableRedaction` (default true), `BearerToken` (default null = random), `LogLevel`, `TransportMode` (based on Phase 0 spike result)
- [ ] `SnoopAgentHandle`: `EndpointUri`, `BearerToken`, `Stop()`
- [ ] Rejects second `Start()` call
- [ ] Stops automatically on `Application.Exit`
- [ ] Port-in-use: auto-fallback to next available
- [ ] `127.0.0.1` only — hardcoded
- [ ] HTTP mode: bearer token required, CORS deny all, query-string tokens rejected
- [ ] JSON discovery file: `%TEMP%\snoop-agent-{pid}.json` with endpoint + token + pid, owner-only ACL, opened with `FILE_FLAG_DELETE_ON_CLOSE` (OS deletes on process exit even on crash)
- [ ] Console output: `SnoopWPF.Agent MCP server listening at {uri}`
- [ ] All tools from US-004 registered

#### US-006: Tool — wpf_get_session_info
- [ ] Returns process info, dispatchers (with window nodeIds), capabilities, mutationEnabled

#### US-007: Tool — wpf_get_windows
- [ ] Input: `{ includeHidden? }`. Output: WindowDto array. Main window first.

#### US-008: Tool — wpf_get_visual_tree
- [ ] Input: `{ rootNodeId?, maxDepth?, treeType?, includeProperties? }`
- [ ] Output: `{ root: NodeDto, truncated, returnedNodeCount }`
- [ ] Nodes at cut boundary have `childrenTruncated: true`
- [ ] Hard cap 5000 nodes
- [ ] `includeProperties` accepts DP names (case-insensitive)

#### US-009: Tool — wpf_get_children
- [ ] Input: `{ nodeId?, treeType?, cursor?, take? }`
- [ ] nodeId optional — omit for app roots
- [ ] Output: `CursorPage<NodeDto>` with stale flag
- [ ] Snapshot-based cursor pagination

#### US-010: Tool — wpf_inspect_element
- [ ] Input: `{ nodeId }`
- [ ] Output includes all summary fields. `triggerCount`/`behaviorCount` nullable (null = not evaluated).

#### US-011: Tool — wpf_get_properties
- [ ] Input: `{ nodeId, filter?, category?, includeDefaults?, cursor?, take? }`
- [ ] Output: `CursorPage<PropertyDto>` with `isRedacted` field
- [ ] Sorted by name

#### US-012: Tool — wpf_get_binding_info
- [ ] Input: `{ nodeId, propertyName }`
- [ ] Output includes `dataContextIsNull`, `dataContextType`, `resolvedValue`

#### US-013: SnoopWPF.Agent.SampleApp

**Acceptance Criteria:**
- [ ] .NET 8 WPF app with: main window, nested layout, bound TextBlock, ListBox, intentional binding error, non-virtualized ListBox, DataTrigger
- [ ] Calls `SnoopAgent.Start()` on startup
- [ ] **`--no-agent` command-line flag** disables the agent (for injection-mode tests)
- [ ] JSON discovery file with endpoint + bearer token
- [ ] `SnoopAgentHandle.BearerToken` available for programmatic test access

#### US-014: Integration test harness — MVP

**Acceptance Criteria:**
- [ ] Launches sample app, reads discovery file (endpoint + token), connects via MCP HTTP client with bearer token
- [ ] Tests: get_session_info, get_windows, get_visual_tree, get_children (with pagination + stale), inspect_element, get_properties, get_binding_info
- [ ] Verifies JSON matches declared schemas
- [ ] Concurrent request test (no deadlocks)
- [ ] Error scenario tests (invalid nodeId, timeout)
- [ ] 30s per test, 5min total

---

### Milestone 3: Full NuGet Tool Set

#### US-015: Tool — wpf_find_elements
- [ ] Input includes `rootNodeId` for subtree scope and `propertyConditions`
- [ ] Output includes `totalScanned`, `truncated`

#### US-016: Tool — wpf_get_ancestors
- [ ] Parent first, root last. Each ancestor includes dataContextType.

#### US-017: Tool — wpf_set_property
- [ ] `MutationDisabled`, `UnsupportedPropertyType`, `TypeConversionFailed` (with `expectedFormat`), `PropertyRedacted` errors
- [ ] Hardcoded converter table, InvariantCulture
- [ ] All mutations logged: nodeId + property name only (NOT before/after values — values may contain secrets)

#### US-018: Tool — wpf_run_diagnostics
- [ ] Subtree scope via nodeId. Cursor pagination. Sorted by level.

#### US-019: Tool — wpf_get_resources
- [ ] Precedence semantics: effective first, shadowed included with origin

#### US-020: Tool — wpf_capture_screenshot
- [ ] Multi-block response: `TextContent` (metadata) + `ImageContent` (base64 PNG)
- [ ] **No temp files, no filePath in response**
- [ ] Fallback: node → MainWindow → first visible window
- [ ] Handles zero-size elements and hidden windows

#### US-021: Tools — wpf_get_triggers and wpf_get_behaviors
- [ ] Separate tool schemas for each
- [ ] BehaviorDto: `{ typeName, assemblyName, properties: {} }`

---

### Milestone 4: Injection Mode

#### US-022: SnoopWPF.Agent.Remote — pipe client proxy

**Acceptance Criteria:**
- [ ] `PipeSnoopInspectorProxy` implementing `ISnoopInspector` over named pipe
- [ ] Framed JSON client matching Injection server protocol
- [ ] Handshake: host sends session token first, agent responds with capabilities
- [ ] `PipeCancel` frame support for in-flight request cancellation
- [ ] Per-operation timeout
- [ ] Unit tests with mock pipe pairs

#### US-023: SnoopWPF.Agent.Injection — injected agent

**Acceptance Criteria:**
- [ ] `net462;net6.0-windows`, ZERO external NuGet deps
- [ ] Exception: `System.IO.Pipes.AccessControl` on net462 for pipe ACLs (documented exception to zero-dep rule)
- [ ] `SnoopAgentEntryPoint.Start(string settingsFile)` matching `ExecuteInDefaultAppDomain` signature
- [ ] Custom framed protocol: `{4-byte LE length}{UTF-8 JSON}`
- [ ] net462: `DataContractJsonSerializer`. net6+: `System.Text.Json`
- [ ] Connects to host pipe, validates session token, verifies via `GetNamedPipeClientProcessId()`
- [ ] Settings file read → token extracted → file deleted → token zeroed after handshake
- [ ] All `ISnoopInspector` methods exposed as pipe RPC
- [ ] Graceful cleanup on disconnect

#### US-024: SnoopWPF.Agent.Host — injection-mode MCP host

**Acceptance Criteria:**
- [ ] `snoop-mcp.exe`, `net8.0-windows`
- [ ] CLI: `snoop-mcp --pid <pid> [--port <port>] [--enable-mutation]`
- [ ] Stdio mode (default): MCP over stdin/stdout
- [ ] HTTP mode (`--http`): bearer token, same rules as NuGet mode
- [ ] Uses `Snoop.Injector` for injection
- [ ] Uses `PipeSnoopInspectorProxy` for RPC
- [ ] Uses `SnoopWPF.Agent.Tools` for MCP tool handlers (same definitions as NuGet mode)
- [ ] Pipe: random GUID + user ACL + 10s connection timeout + client PID verification
- [ ] Bundles all 3 `Snoop.InjectorLauncher.{arch}.exe` binaries
- [ ] Graceful shutdown on target exit / Ctrl+C

#### US-025: SnoopWPF.Agent.CLI

**Acceptance Criteria:**
- [ ] `snoop-cli.exe`, `net8.0-windows`
- [ ] Stateless: each command auto-injects, queries, disconnects
- [ ] Commands: `session`, `windows`, `tree`, `children`, `ancestors`, `find`, `inspect`, `properties`, `set-property`, `binding`, `diagnostics`, `resources`, `screenshot`, `triggers`, `behaviors`
- [ ] Global options: `--pid <pid>`, `--format json|table|tree`, `--port <port>` (connect to existing MCP server instead of injecting)
- [ ] `--pid` vs `--port`: `--port` takes precedence (skip injection, connect to existing server)
- [ ] `--format table` via Spectre.Console
- [ ] Exit code 0/non-zero

#### US-026: Injection mode integration tests

**Acceptance Criteria:**
- [ ] Launch sample app with `--no-agent`, inject via snoop-mcp
- [ ] Exercise: get_windows, get_visual_tree, get_properties, get_binding_info
- [ ] Verify injection into net8.0 target works

---

### Milestone 5: Release

#### US-027: Build system and CI

**Acceptance Criteria:**
- [ ] All projects in `Snoop.sln`
- [ ] Nuke `Build.cs` updated
- [ ] GitHub Actions: build + test on `windows-latest`
- [ ] NuGet package `SnoopWPF.Agent` produced with correct metadata
- [ ] `snoop-mcp.exe` and `snoop-cli.exe` as artifacts (with InjectorLauncher binaries)
- [ ] `LangVersion` set to `latest` in new projects
- [ ] NuGet lock files enabled (`RestoreLockedMode = true`)

#### US-028: Documentation and security

**Acceptance Criteria:**
- [ ] README.md: NuGet quick-start (3 lines), injection mode, Claude Code config for both transports
- [ ] SECURITY.md: vulnerability disclosure, security contact, mutation/redaction docs, "reading properties executes target app getters" warning, TypeConverter whitelist, no method invocation in v1
- [ ] CHANGELOG.md
- [ ] NuGet: license (MS-PL), description, tags, icon
- [ ] Verify `SnoopWPF.Agent` name available on nuget.org
- [ ] State this PRD supersedes TRANSFORMATION_PLAN.md

---

## Story Dependencies

```
US-000a-d (Spikes — parallel, first)
  │
  v
US-001 (Contracts)
  ├──> US-002 (Snoop.Injector)
  ├──> US-003  (Engine: tree + properties)
  │    ├──> US-003b (diagnostics/resources/screenshots)
  │    ├──> US-003c (triggers/behaviors)
  │    ├──> US-003d (SnoopManager headless)
  │    └──> US-003e (multi-dispatcher — OPTIONAL for MVP)
  │
  ├──> US-004 (Shared tool handlers) [needs US-003]
  │    └──> US-005 (NuGet MCP server)
  │         ├──> US-006..US-012 (MVP tools — parallelizable)
  │         └──> US-013 (Sample app) [needs US-005]
  │              └──> US-014 (Integration tests) [needs US-006..US-012]
  │
  │ [Milestone 3 — parallelizable, need US-005]
  ├──> US-015 (find_elements)
  ├──> US-016 (get_ancestors)
  ├──> US-017 (set_property)
  ├──> US-018 (diagnostics)     [needs US-003b]
  ├──> US-019 (resources)       [needs US-003b]
  ├──> US-020 (screenshot)      [needs US-003b]
  └──> US-021 (triggers/behaviors) [needs US-003c]

US-003d + US-002 ──> US-022 (Remote proxy)
                      ├──> US-023 (Injection agent)
                      │    └──> US-024 (Host) [needs US-004 for shared tools]
                      └──> US-025 (CLI) [needs US-022]
                            └──> US-026 (Injection integration tests) [needs US-013 --no-agent]

US-026 + US-021 ──> US-027 (Build/CI) ──> US-028 (Docs/Release)
```

**Critical path to MVP demo:** US-001 → US-003 → US-004 → US-005 → US-008 (get_visual_tree) → US-013 → US-014

---

## Testing Plan

### Unit Tests
- DTO serialization round-trip (every type, both `System.Text.Json` and `DataContractJsonSerializer`)
- SnoopInspector DTO projection (mock TreeService/PropertyInformation)
- Node registry: forward + reverse lookup, GC behavior, lazy pruning
- Cursor pagination: snapshot, stale detection, boundary cases
- Error mapping: SnoopException → MCP error + suggestion
- Pipe protocol: framing, max frame enforcement, cancel frame, malformed messages
- Redaction: contains-match on all keywords, SecureString detection, getter-skip behavior
- TypeConverter whitelist: safe types accepted, unsafe rejected, hardcoded table not TypeDescriptor

### Integration Tests
- Full workflow: get_windows → get_visual_tree → inspect_element → get_properties → get_binding_info
- Pagination: cursor-based with stale detection
- Property set + verify (with mutation enabled)
- Diagnostics: detect known issues in sample app
- Screenshot: verify MCP ImageContent block renders correctly
- Concurrent requests: no deadlocks
- Error scenarios: invalid nodeId, GC'd element, timeout, mutation disabled
- Injection mode: inject → get tree → get properties (Milestone 4)

### CI Strategy
- `windows-latest` GitHub Actions
- WPF apps launch headlessly (tree/property works without display)
- Screenshot tests conditional (may need skip in headless)
- Mock pipe pairs for transport unit tests (no real injection)
- NuGet lock files for reproducible builds

---

## Risk Register

| # | Risk | L | I | Mitigation |
|---|------|---|---|-----------|
| 1 | Claude Code can't connect to HTTP/SSE | M | C | **Phase 0 spike.** Fallback: stdio bridge. |
| 2 | WPF inspection fails headlessly in CI | M | H | **Phase 0 spike.** Conditional test categories. |
| 3 | Injection dependency conflicts | M | H | Zero external deps. Custom protocol. |
| 4 | Dispatcher deadlock | M | H | DispatcherPriority.Send, exclusive queue, timeout |
| 5 | Large trees overwhelm | M | M | 5000-node cap, cursor pagination, take<=200 |
| 6 | SnoopManager refactor larger than expected | M | M | Spike US-000b. IInjectedAgent isolates changes. |
| 7 | Cursor stale on dynamic trees | M | M | Snapshot cursors with 30s TTL + stale flag |
| 8 | Browser JS accesses HTTP server | M | H | Bearer token, CORS deny, query-string rejection |
| 9 | Secrets in property dumps | H | H | Contains-match redaction, getter skipped |
| 10 | TypeConverter gadgets | L | H | Hardcoded converter table, never TypeDescriptor |
| 11 | Upstream Snoop conflicts | L | M | Minimal changes; new code in separate projects |
| 12 | net462 pipe ACLs | M | M | System.IO.Pipes.AccessControl exception; spike US-000d |

---

## Effort Estimate

| Milestone | Estimate |
|-----------|----------|
| Phase 0 Spikes | 1 week |
| M1: Foundation | 2-3 weeks |
| M2: MVP Tools + Tests | 2-3 weeks |
| M3: Full Tool Set | 2-3 weeks |
| M4: Injection Mode | 3-4 weeks |
| M5: Release | 1 week |
| **Total (1 senior engineer)** | **13-18 weeks** |
| **Total (2 engineers)** | **10-14 weeks** |

---

## Open Questions

### Must answer before implementation
1. **Transport compatibility** — does Claude Code connect to HTTP/SSE + bearer token? (Phase 0 spike)
2. **Headless CI** — does WPF tree inspection work on `windows-latest`? (Phase 0 spike)

### Answer before Milestone 3
3. **Package name** — is `SnoopWPF.Agent` available on nuget.org?

### Deferrable
4. **Upstream relationship** — discuss with Snoop maintainers after PoC
5. **Event monitoring** — bounded capture vs streaming; revisit after real usage
6. **net462 in NuGet** — only needed for injection; NuGet consumers are net8+
