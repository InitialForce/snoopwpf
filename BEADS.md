# SnoopWPF.Agent — Implementation Beads v6 (FINAL)

> Generated from PRD v3 (`PRD.md`). Polished via 8 review rounds (23x Opus + 1x GPT-5.4-Pro).
> Covers: executability, dependencies, PRD fidelity, sizing, testing, file paths, security/threading,
> architecture, MCP compliance, injection pipeline, CI feasibility, serialization, disposal, packaging, red team.
> Each bead is one atomic work unit completable in a single agent session (~100K token context).
> Beads ordered by dependency. Execute in order; parallelizable beads noted.
> **Cross-reference:** See `PRD.md` §Key Tool Schemas for all DTO field definitions.
>
> **Global build rules (apply to ALL new .csproj files):**
> - **Every new .csproj MUST include `<LangVersion>latest</LangVersion>`** — `Directory.build.props` sets `LangVersion=10` globally, which breaks C# 12 primary constructors used in the MCP tool pattern. Override per-project until BEAD-027 updates the global setting.
> - **Every new NuGet dependency MUST have a `<PackageVersion>` entry in `Directory.packages.props`** — the repo uses Central Package Management (`ManagePackageVersionsCentrally=true`). A `<PackageReference>` without a matching `<PackageVersion>` causes NU1104 at restore.
> - **New test projects target `net8.0-windows`** — to reference `SnoopWPF.Agent.Tools` (net8.0-windows only).
>
> **Global security rules (apply to ALL beads):**
> - **NEVER call `TypeDescriptor.GetConverter()`** — all type conversion uses hardcoded converter table (BEAD-017). DtoProjection must use `PropertyInformation.StringValue` or `value.ToString()` for display, never TypeDescriptor.
> - **All WPF object access must happen on the Dispatcher** — DtoProjection, TriggerInspector, BehaviorInspector, ScreenshotCapture are helpers that are always called from within a `Dispatcher.Invoke` block in SnoopInspector. They must NOT provide their own async entry points or access WPF objects independently.
> - **Property values are never logged** — exception payloads must be sanitized before logging (strip paths, tokens, property values).
> - **Concurrency cap:** SnoopInspector must use a `SemaphoreSlim(maxConcurrency: 3)` around Dispatcher dispatch to prevent starvation of the UI thread. At most 3 MCP requests may be in-flight on the Dispatcher simultaneously.
> - **For non-DependencyObject nodes:** use `type.GetProperties(BindingFlags.Public | BindingFlags.Instance)` (reflection) NOT `TypeDescriptor.GetProperties()` — the latter activates registered `TypeDescriptionProvider`s which may invoke custom converters.
> - **Target process ownership check (injection mode):** Before injection, verify target process owner SID matches current user. Emit warning and require `--force` flag if mismatched. (BEAD-024)

---

## Quality Gates

These commands must pass after every bead:
```bash
dotnet build Snoop.sln          # Full solution builds
dotnet test Snoop.Core.Tests    # Existing tests pass
```

After Milestone 1 (BEAD-003-infra onward):
```bash
dotnet test SnoopWPF.Agent.Tests              # Unit tests pass
dotnet test Snoop.Core.Tests                   # Existing tests still pass (BEAD-003d modifies Snoop.Core)
```

After Milestone 2 (BEAD-014 onward):
```bash
dotnet test SnoopWPF.Agent.IntegrationTests   # Integration tests pass
```

---

## Phase 0: Spikes

> BEAD-000a, BEAD-000b, BEAD-000d are fully parallel. BEAD-000c depends on BEAD-000b (reuses its app).

### BEAD-000a: Transport Spike — Verify MCP SDK with stdio and HTTP/SSE

**PRD ref:** US-000a
**Goal:** Confirm Claude Code connects to both MCP transports before committing to architecture.
**Estimated:** 3 .cs files + NOTES.md (~150 LOC)

**Steps:**
1. Create `spike/TransportSpike/` folder (outside solution — throwaway)
2. Create minimal .NET 8 console app. **Important:** This project uses Central Package Management — add `<PackageVersion Include="ModelContextProtocol" Version="X.Y.Z" />` and `<PackageVersion Include="ModelContextProtocol.AspNetCore" Version="X.Y.Z" />` to `Directory.packages.props` (check nuget.org for latest stable 1.x). The spike `.csproj` uses `<PackageReference Include="ModelContextProtocol" />` without version.
3. Implement one dummy MCP tool (`echo`) that returns its input
4. Test **stdio** transport: configure in Claude Code `claude_desktop_config.json`, verify tool call works
5. Test **HTTP/SSE** transport: start Kestrel on `127.0.0.1`, configure bearer token, verify Claude Code connects
6. Test `ImageContent` block: return a small PNG base64 from a tool, verify Claude renders it
7. Record pinned `ModelContextProtocol` version in `spike/TransportSpike/NOTES.md`
8. Record decision: which transport for NuGet mode, which for injection mode

**Acceptance Criteria:**
- [ ] stdio transport: Claude Code calls dummy tool and gets response
- [ ] HTTP/SSE transport: Claude Code connects with bearer token and calls tool
- [ ] ImageContent: Claude renders inline image from tool response
- [ ] `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` versions pinned in `Directory.packages.props`
- [ ] Decision recorded in `spike/TransportSpike/NOTES.md`

---

### BEAD-000b: In-Process Engine Spike — Verify headless Snoop.Core tree traversal

**PRD ref:** US-000b
**Goal:** Prove that `TreeService`, `PropertyInformation`, and DTO projection work headlessly in-process.
**Estimated:** 2 .cs files + NOTES.md (~80 LOC)

**Steps:**
1. Create `spike/EngineSpike/` — .NET 8 WPF app with a Button
2. On `Loaded`, call `var treeService = TreeService.From(TreeType.Visual);` then `var root = treeService.Construct(Application.Current, parent: null);`. Walk `root.Children` to enumerate visual tree nodes.
3. Read types and names from each `TreeItem`
4. Call `var props = PropertyInformation.GetProperties(someElement);` to get all properties. Read `props[0].StringValue` on the Dispatcher. Call `Teardown()` on each `PropertyInformation` when done — all in a single `Dispatcher.Invoke`.
5. Project to a simple DTO record, verify fields populated
6. Verify no deadlocks, no crashes

**Acceptance Criteria:**
- [ ] `TreeService.From(TreeType.Visual).Construct(Application.Current, null)` returns children
- [ ] `PropertyInformation.GetProperties(element)` reads property values without deadlock
- [ ] DTO projection populates type, name, value fields
- [ ] No crashes or hangs

**Deliverable:** `spike/EngineSpike/NOTES.md`

---

### BEAD-000c: Headless CI Spike — WPF on GitHub Actions

**PRD ref:** US-000c
**Depends on:** BEAD-000b (reuses its WPF app)
**Goal:** Determine if WPF tree inspection works on `windows-latest` (no display).
**Estimated:** 1 workflow YAML + NOTES.md (~40 LOC)

**Steps:**
1. Create `.github/workflows/spike-headless.yml`
2. Launch BEAD-000b spike app on `windows-latest`
3. Run tree traversal and property inspection — document results
4. Test screenshot capture — document results (may fail headlessly)
5. Test with both net6.0 and net8.0 TargetFramework

**Acceptance Criteria:**
- [ ] WPF app launches on `windows-latest`
- [ ] Tree traversal works without display
- [ ] Screenshot results documented (pass or documented limitation)
- [ ] Both net6.0 and net8.0 tested

**Deliverable:** `spike/NOTES-headless-ci.md`

---

### BEAD-000d: Injection Transport Spike — Named pipe protocol

**PRD ref:** US-000d
**Goal:** Verify framed JSON over named pipes works across net462/net6.0/net8.0.
**Estimated:** 4 .cs files + NOTES.md (~200 LOC)

**Steps:**
1. Create `spike/PipeSpike/` — two console apps (host + client)
2. Host creates `NamedPipeServerStream` with `PipeOptions.CurrentUserOnly` (net6+)
3. Client connects, roundtrip `{4-byte LE length}{UTF-8 JSON}`
4. Also test net462 path: `PipeSecurity`/`PipeAccessRule` (no `PipeOptions.CurrentUserOnly`)
5. Test cancel frame (`{"id": 1, "cancel": true}`)
6. Enforce max 10MB frame size — test rejection
7. Measure latency for typical payloads (100 nodes, 80 properties)

**Acceptance Criteria:**
- [ ] net8.0 host ↔ net6.0 client: handshake + request/response works
- [ ] net462 pipe ACLs work (PipeSecurity path)
- [ ] Cancel frame delivered and processed
- [ ] 10MB frame size enforced — oversized frame rejected gracefully

**Deliverable:** `spike/PipeSpike/NOTES.md`

---

## Milestone 1: Foundation

### BEAD-001: SnoopWPF.Agent.Contracts — shared protocol types

**PRD ref:** US-001
**Blocks:** BEAD-003-infra, BEAD-003d, BEAD-004a, BEAD-022, BEAD-023
**Goal:** Create the shared contracts library — DTOs, interfaces, error codes, protocol types.
**Estimated:** 24 files, ~500 LOC (pure data types, no WPF internals)

**Files to create:**
- `SnoopWPF.Agent.Contracts/SnoopWPF.Agent.Contracts.csproj`
- `SnoopWPF.Agent.Contracts/DTOs/*.cs` (14 DTO files)
- `SnoopWPF.Agent.Contracts/ISnoopInspector.cs`
- `SnoopWPF.Agent.Contracts/SnoopErrorCode.cs`
- `SnoopWPF.Agent.Contracts/SnoopException.cs`
- `SnoopWPF.Agent.Contracts/CursorPage.cs`
- `SnoopWPF.Agent.Contracts/Protocol/*.cs` (5 protocol files)

**Steps:**
1. Create `SnoopWPF.Agent.Contracts.csproj` — `netstandard2.0`, ZERO dependencies
2. Create all DTO classes as **mutable POCOs** with parameterless constructors and settable properties (NOT records). **Serializer attributes ARE required** — add `[DataContract]` on each class and `[DataMember(Name = "camelCaseName")]` on each property. These attributes are in `System.Runtime.Serialization.Primitives` (part of netstandard2.0 — zero external NuGet deps). The `Name` parameter on `[DataMember]` forces DCJS (net462) to emit camelCase matching STJ's `CamelCase` policy. **Do NOT use `[JsonPropertyName]`** (STJ-only, not in netstandard2.0).
   **Dictionary replacement:** Replace `Dictionary<string,string>` fields with `List<NameValuePairDto>` (where `NameValuePairDto` has `Name` + `Value` strings) — DCJS serializes `Dictionary` as a JSON array of `{Key,Value}` pairs, incompatible with STJ's JSON object format.
3. Run `dotnet sln Snoop.sln add SnoopWPF.Agent.Contracts/SnoopWPF.Agent.Contracts.csproj` — flat structure, no solution folder

**DTO field specifications** (all fields are settable properties):

`NodeDto`: NodeId (string), TypeName, Name, DisplayName, ChildCount (int), HasBindingError (bool), Depth (int), ChildrenTruncated (bool), Properties (List<NameValuePairDto>, nullable), Children (List<NodeDto>, nullable)
`NameValuePairDto`: Name (string), Value (string) — replaces Dictionary for DCJS/STJ compatibility

`PropertyDto`: Name, TypeName, Value, ValueSource, IsLocallySet (bool), IsDataBound (bool), HasBindingError (bool), BindingError (string, nullable), IsReadOnly (bool), HasTypeConverter (bool), IsRedacted (bool)

`BindingInfoDto`: HasBinding (bool), BindingType, Path, ElementName, RelativeSource, Mode, UpdateSourceTrigger, ConverterTypeName, SourceType, Status, Error (nullable), DataContextIsNull (bool), DataContextType, ResolvedValue (string, nullable), ChildBindings (List<BindingInfoDto>, nullable)

`DiagnosticItemDto`: Name, Description, Area, Level, NodeId, NodePath (List<string>)

`ResourceDto`: Key, ValueTypeName, ValueSummary, Origin, DictionarySource

`TriggerDto`: TriggerType, IsActive (bool), Source (string: "Style", "ControlTemplate", "DataTemplate", or "Element" — matches `TriggerSource` enum values in Snoop.Core), Conditions (List<TriggerConditionDto>), Setters (List<TriggerSetterDto>)
— Also create `TriggerConditionDto`: Property, Value
— Also create `TriggerSetterDto`: Property, Value

`BehaviorDto`: TypeName, AssemblyName, Properties (List<NameValuePairDto>)

`WindowDto`: NodeId (string), Title, TypeName, Width (double), Height (double), DispatcherId (int)

`SetPropertyResultDto`: Success (bool), PreviousValue, NewValue, Error (string, nullable)

`ScreenshotMetadataDto`: Width (int), Height (int), NodeId (string)

`SessionInfoDto`: ProcessName, Pid (int), DotnetVersion, MutationEnabled (bool), Dispatchers (List<DispatcherInfoDto>), Capabilities (List<string>)

`DispatcherInfoDto`: Id (int), ThreadId (int), WindowNodeIds (List<string>)

`FindElementResultDto`: Results (List<FindElementHitDto>), TotalScanned (int), Truncated (bool)
— Also create `FindElementHitDto`: Node (NodeDto), Path (List<string>)

`InspectElementDto`: NodeId (string), TypeName, Name, DisplayName, Path (List<string>), ParentNodeId, ChildCount (int), Depth (int), DispatcherId (int), IsVisible (bool), ActualWidth (double), ActualHeight (double), DataContextType, HasBindingErrors (bool), BindingErrorCount (int), TriggerCount (int?, nullable), BehaviorCount (int?, nullable)

4. Create `ISnoopInspector` interface with these exact 15 method signatures:
```csharp
Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct);
Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct);
Task<VisualTreeResultDto> GetVisualTreeAsync(string? rootNodeId, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct);
Task<CursorPage<NodeDto>> GetChildrenAsync(string? nodeId, string treeType, string? cursor, int take, CancellationToken ct);
Task<List<AncestorDto>> GetAncestorsAsync(string nodeId, int? maxLevels, CancellationToken ct);
Task<FindElementResultDto> FindElementsAsync(string? typeName, string? name, string? rootNodeId, List<PropertyConditionDto>? conditions, string treeType, int maxResults, CancellationToken ct);
Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct);
Task<CursorPage<PropertyDto>> GetPropertiesAsync(string nodeId, string? filter, string? category, bool includeDefaults, string? cursor, int take, CancellationToken ct);
Task<SetPropertyResultDto> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct);
Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct);
Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(string? nodeId, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct);
Task<CursorPage<ResourceDto>> GetResourcesAsync(string? nodeId, string? resourceKey, string? cursor, int take, CancellationToken ct);
Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct);
Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct);
Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct);
```
   Also create these additional DTOs (not listed above):
   - `VisualTreeResultDto`: Root (NodeDto), Truncated (bool), ReturnedNodeCount (int)
   - `AncestorDto`: NodeId (string), TypeName, Name, DataContextType
   - `PropertyConditionDto`: Property (string), Operator ("Equals"|"Contains"), Value (string)
   - `ScreenshotResultDto`: Metadata (ScreenshotMetadataDto), PngBytes (byte[])
5. Create `SnoopErrorCode` enum: `NodeNotFound`, `DispatcherBusy`, `OperationTimedOut`, `PropertyReadOnly`, `TypeConversionFailed`, `UnsupportedPropertyType`, `MutationDisabled`, `PropertyRedacted`, `SessionNotFound`, `ProtocolMismatch`, `ElementNotRenderable` (11 codes)
6. Create `SnoopException` with Code, Message, Suggestion properties
7. Create `SnoopSuggestions` static class with canonical suggestion strings from PRD (one per error code):
   - `NodeNotFound` → "Re-navigate from wpf_get_windows — element was likely garbage collected"
   - `DispatcherBusy` → "Retry the call; if repeated, the WPF app may be performing a long UI operation"
   - `OperationTimedOut` → "Reduce scope (smaller subtree, fewer properties) or retry when app is idle"
   - `PropertyReadOnly` → "This property cannot be set; use wpf_get_properties to find writable properties"
   - `TypeConversionFailed` → "Check value format; Color=#RRGGBB or named; Thickness=L,T,R,B; see tool description"
   - `UnsupportedPropertyType` → "Only primitive and common WPF value types are settable; see tool description for list"
   - `MutationDisabled` → "Mutations disabled; set EnableMutation=true in SnoopAgentOptions to allow changes"
   - `PropertyRedacted` → "This property is redacted for security; its value cannot be read or set"
   - `SessionNotFound` → "No active session; the target process may have exited"
   - `ProtocolMismatch` → "Agent and host protocol versions differ; update to matching versions"
   - `ElementNotRenderable` → "Element has zero size or is not visible; try wpf_get_windows for a full window screenshot instead"
8. Create `CursorPage<T>`: Items (List<T>), NextCursor (string, nullable), TotalCount (int), HasMore (bool), Stale (bool), Truncated (bool)
9. Create protocol types:
   - `PipeRequest`: Id (int), Method (string), ParamsJson (string — raw JSON of method parameters)
   - `PipeResponse`: Id (int), ResultJson (string, nullable — raw JSON of result), Error (PipeError, nullable)
   - `PipeError`: Code (string), Message (string), Suggestion (string)
   - `PipeCancel`: Id (int), Cancel (bool — always true on wire)
   - `HandshakeChallenge`: SessionToken (string), ProtocolVersion (int) — sent by host
   - `HandshakeResponse`: ProtocolVersion (int), AgentVersion (string), TargetRuntime (string), SessionToken (string), Dispatchers (List<DispatcherInfoDto>), Capabilities (List<string>) — sent by agent
   Note: `ParamsJson`/`ResultJson` are raw JSON strings, not escaped. The transport layer parses method-specific types.
10. Create `ProtocolConstants`: MaxFrameSize = 10_485_760, ProtocolVersion = 1

**Acceptance Criteria:**
- [ ] `netstandard2.0`, zero NuGet deps
- [ ] All DTOs are mutable POCOs with parameterless constructors, `[DataContract]` class attr, `[DataMember(Name="camelCase")]` on all properties
- [ ] No `Dictionary<K,V>` in any DTO — use `List<NameValuePairDto>` instead
- [ ] All DTO fields match specs above (especially `BindingInfoDto.ChildBindings` list)
- [ ] `ISnoopInspector` has all 15 methods, all `Task`-based with `CancellationToken`
- [ ] All 11 error codes defined
- [ ] `CursorPage<T>` has all 6 fields
- [ ] Protocol types defined
- [ ] `dotnet build Snoop.sln` passes

---

### BEAD-002: Snoop.Injector — extract injection orchestration from Snoop GUI

**PRD ref:** US-002
**Blocks:** BEAD-024, BEAD-025
**Depends on:** nothing (parallel with BEAD-001)
**Goal:** Extract injection logic from `Snoop/` GUI project into a reusable library.
**Estimated:** 1 new .csproj + moves, ~80 LOC new code

**Source files to move:**
- `Snoop/InjectorLauncherManager.cs` → `Snoop.Injector/InjectorLauncherManager.cs`
- `Snoop/ProcessInfo.cs` → `Snoop.Injector/ProcessInfo.cs`
- `Snoop/WindowInfo.cs` → `Snoop.Injector/WindowInfo.cs`

**Files to modify:**
- `Snoop/Snoop.csproj` — add `<ProjectReference>` to `Snoop.Injector`, remove moved files
- `Snoop.sln` — `dotnet sln add Snoop.Injector/Snoop.Injector.csproj`

**Steps:**
1. Create `Snoop.Injector/Snoop.Injector.csproj` targeting `net462;net6.0-windows;net8.0-windows`
2. Move files (see above). **IMPORTANT:** `ProcessInfo.Snoop()` currently uses `typeof(SnoopManager).GetMethod(nameof(SnoopManager.StartSnoop))` — NOT string constants. Change it to use the `InjectorLauncherManager.Launch(processInfo, hwnd, assemblyName, className, methodName, settingsFile)` overload with literal strings `"Snoop.Core"`, `"Snoop.Infrastructure.SnoopManager"`, `"StartSnoop"`. This removes the `Snoop.Core` type dependency from `Snoop.Injector`.
3. Include InjectorLauncher binaries (x86, x64, ARM64). **Note:** The existing codebase handles binary distribution via the Nuke build in `.build/Build.cs`, NOT via `<Content>` items in `Snoop.csproj`. Check `.build/Build.cs` for the existing copy/distribution pattern and replicate it. `InjectorLauncherManager` resolves them by `Path.Combine(directory, $"Snoop.InjectorLauncher.{architecture}.exe")` where `directory` is the exe's directory. For the new `Snoop.Injector` project, add `<Content Include="..." CopyToOutputDirectory="PreserveNewest" />` items pointing to the build output, OR extend the Nuke build to copy them.
5. Update `Snoop/Snoop.csproj` to reference `Snoop.Injector`
6. Verify `dotnet build Snoop.sln` and `dotnet test Snoop.Core.Tests` pass

**Acceptance Criteria:**
- [ ] `Snoop.Injector.csproj` targets `net462;net6.0-windows;net8.0-windows`
- [ ] All injection code moved from `Snoop/`
- [ ] `ProcessInfo` uses string constants (no `typeof(SnoopManager)`)
- [ ] `Snoop/` references `Snoop.Injector`
- [ ] InjectorLauncher binaries included as content files
- [ ] `dotnet build Snoop.sln` passes
- [ ] `dotnet test Snoop.Core.Tests` passes

---

### BEAD-003-infra: Engine infrastructure — node registry, cursors, redaction, DTO projection

**PRD ref:** US-003 (part 1 of 2)
**Depends on:** BEAD-001
**Blocks:** BEAD-003-inspector
**Goal:** Create the engine project and implement the infrastructure classes that SnoopInspector depends on. No Dispatcher or WPF knowledge needed.
**Estimated:** 8 files + .csproj, ~400 LOC

**Files to create:**
- `SnoopWPF.Agent.Engine/SnoopWPF.Agent.Engine.csproj` (targets `net462;net6.0-windows;net8.0-windows`, UseWpf=true)
- `SnoopWPF.Agent.Engine/NodeRegistry.cs`
- `SnoopWPF.Agent.Engine/CursorManager.cs`
- `SnoopWPF.Agent.Engine/RedactionFilter.cs`
- `SnoopWPF.Agent.Engine/DtoProjection.cs`
- `SnoopWPF.Agent.Tests/SnoopWPF.Agent.Tests.csproj`
- `SnoopWPF.Agent.Tests/NodeRegistryTests.cs`
- `SnoopWPF.Agent.Tests/CursorManagerTests.cs`
- `SnoopWPF.Agent.Tests/RedactionFilterTests.cs`

**Steps:**
1. Create `SnoopWPF.Agent.Engine.csproj`. References: `Snoop.Core`, `SnoopWPF.Agent.Contracts`.
2. **NodeRegistry:**
   - `ConditionalWeakTable<object, NodeRegistration>` (forward: object → nodeId)
   - `ConcurrentDictionary<string, WeakReference<object>>` (reverse: nodeId string → object). **Must be ConcurrentDictionary** — the 60s timer sweep iterates it concurrently with Dispatcher-thread registrations. Keyed by full node ID string `"0:42"` (not int — future multi-dispatcher compatible).
   - Monotonic counter. Single-dispatcher: always `0:{counter}`.
   - Lazy pruning on reverse lookup access: when `TryGetTarget()` fails, remove entry.
   - 60s periodic sweep: `System.Threading.Timer` callback iterates reverse dict, removes dead entries. **No WPF API access in sweep** — `TryGetTarget()` only reads the reference, it does not access the WPF object. Cancel timer in `Dispose()`.
   - **Testability:** Accept `TimeSpan sweepInterval` in constructor (default 60s) so tests can control sweep timing. Expose `internal void ForceSweep()` for unit tests.
3. **CursorManager:**
   - Snapshot: stores `string[]` of child nodeIds, keyed by opaque cursor token
   - 30s TTL per snapshot. Returns `stale: true` when snapshot expired (regenerate fresh page).
   - Thread-safe (ConcurrentDictionary or lock)
   - **Testability:** Accept `Func<DateTimeOffset> clock` in constructor (default `DateTimeOffset.UtcNow`) so tests can control time without sleeping 30s.
4. **RedactionFilter:**
   - Contains-match (case-insensitive) on: `password`, `passwd`, `pwd`, `secret`, `apikey`, `connectionstring`, `connstr`, `credential`, `privatekey`, `sharedkey`, `cookie`, `sessionkey`, `authorization`, `authtoken`, `authkey`, `accesstoken`, `bearertoken`, `refreshtoken`, `sessiontoken`, `sastoken`, `jwttoken`
   - Also: all `SecureString`-typed properties, `PasswordBox.Password`
   - `bool IsRedacted(string propertyName, Type propertyType)` — pure function, no WPF dependency
5. **DtoProjection:** Static helper methods to convert Snoop types (`TreeItem`, `PropertyInformation`) to DTOs (`NodeDto`, `PropertyDto`). Pure mapping — **these methods must ONLY be called from within a Dispatcher.Invoke block in SnoopInspector (next bead). They are NOT thread-safe and access live WPF objects.** Use `propInfo.StringValue` or `value.ToString()` for display formatting — NEVER call `TypeDescriptor.GetConverter()` (see global security rules at top).
6. Create `SnoopWPF.Agent.Tests` project with unit tests for all above.
7. Add both projects to `Snoop.sln`.

**Acceptance Criteria:**
- [ ] Engine project targets `net462;net6.0-windows;net8.0-windows`
- [ ] NodeRegistry: `IDisposable`, forward + reverse lookup works; GC'd objects pruned; 60s timer sweep works; reverse dict size cap (trigger `ForceSweep()` at 10,000 entries)
- [ ] CursorManager: `IDisposable`, snapshot cached, stale after 30s, thread-safe, **has background sweep** (same pattern as NodeRegistry) to evict expired entries — prevents unbounded accumulation in long sessions
- [ ] RedactionFilter: all 21 keywords match by contains (case-insensitive); SecureString/PasswordBox detected
- [ ] DtoProjection: converts TreeItem → NodeDto, PropertyInformation → PropertyDto with correct fields
- [ ] `dotnet test SnoopWPF.Agent.Tests` passes
- [ ] `dotnet build Snoop.sln` passes

---

### BEAD-003-inspector: Engine — SnoopInspector tree + property methods

**PRD ref:** US-003 (part 2 of 2)
**Depends on:** BEAD-003-infra
**Blocks:** BEAD-003b, BEAD-003c, BEAD-004a
**Goal:** Implement the SnoopInspector class with Dispatcher marshaling, tree traversal, and property inspection.
**Estimated:** 2 files, ~400 LOC (requires deep understanding of PropertyInformation and TreeService)

**Files to create:**
- `SnoopWPF.Agent.Engine/SnoopInspector.cs`
- `SnoopWPF.Agent.Engine/SnoopInspectorOptions.cs`

**Key Snoop.Core classes to understand first:**
- `Snoop.Core/Infrastructure/PropertyInformation.cs` (~1066 lines) — IS a DependencyObject with live WPF bindings
- `Snoop.Core/Data/Tree/TreeService.cs` — abstract; use `TreeService.From(TreeType)` for concrete instances (VisualTreeService, LogicalTreeService, AutomationPeerTreeService)
- `Snoop.Core/Infrastructure/Diagnostics/DiagnosticContext.cs`

**Steps:**
1. Create `SnoopInspectorOptions`: `TimeoutMs (default 5000)`, `EnableMutation (default false)`, `EnableRedaction (default true)`
2. Create `SnoopInspector` implementing `ISnoopInspector`:
   - Constructor: `SnoopInspector(Dispatcher dispatcher, object? rootTarget = null, SnoopInspectorOptions? options = null)`. `rootTarget` is `Application.Current` in NuGet mode or injection root in injection mode.
   - **Threading pattern (CRITICAL — be precise):**
     - **Dispatcher shutdown guard:** At entry of every method, check `if (dispatcher.HasShutdownStarted) throw new SnoopException(SessionNotFound, ...)`. Also add `Debug.Assert(!dispatcher.CheckAccess(), "SnoopInspector methods must not be called from the Dispatcher thread")` to prevent deadlocks.
     - Each method queues work via `await Dispatcher.InvokeAsync(() => { /* synchronous work block */ }, DispatcherPriority.Send)`. Inside the lambda, all WPF access is a single synchronous block. Do NOT use multiple awaited `InvokeAsync` calls for work that must be atomic.
     - Timeout: wrap with `Task.WhenAny(dispatcherTask, Task.Delay(TimeoutMs))` — if timeout wins, throw `SnoopException(DispatcherBusy)` if work hasn't started, or `SnoopException(OperationTimedOut)` if work started but didn't finish.
     - **SnoopInspector must NOT be called from the Dispatcher thread** — the `await InvokeAsync` pattern requires a non-Dispatcher caller. Integration tests must run on a separate thread.
3. **Tree traversal:** `var treeService = TreeService.From(treeType); var root = treeService.Construct(target, parent: null);` Walk `root.Children`. **Dispose the `TreeService` after each call** — it holds `DiagnosticContext` event subscriptions that leak if not disposed. Note: `Construct()` internally calls `Reload()` which triggers side effects on the target app (layout passes, `ExpandTo()` on children). This is expected Snoop behavior.
4. **Property inspection lifecycle (CRITICAL — must be ONE synchronous block inside one `Dispatcher.InvokeAsync` lambda):**
   - `var props = PropertyInformation.GetProperties(target);`
   - Read values from each `PropertyInformation` (check `RedactionFilter.IsRedacted` FIRST — skip getter for redacted props, return `[REDACTED]`)
   - Project to `PropertyDto[]` via `DtoProjection` (uses `propInfo.StringValue` or `value.ToString()`, NEVER `TypeDescriptor.GetConverter()`)
   - Call `Teardown()` on each `PropertyInformation`
   - **After `Teardown()`: stop orphaned timers.** `PropertyInformation` constructor starts a `DispatcherTimer` via `OnValueChanged`. `Teardown()` calls `BindingOperations.ClearAllBindings()` but does NOT stop the timer. After teardown, check if `changeTimer` field (private) is non-null via reflection and call `Stop()`. Or: add a `StopChangeTimer()` method to `PropertyInformation` in Snoop.Core (small additive change).
   - Return projected DTOs
   - **All of the above in ONE synchronous block** — no interleaving allowed
5. **Path alias support:** Accept node addresses as `Window\Grid\StackPanel\Button` (backslash-separated type names). Resolve by walking tree from root matching each segment by type name.
6. Properties sorted by name for stable cursor pagination
7. **`includeProperties` on GetVisualTreeAsync:** When `includeProperties` is non-null (max 10 entries), for each tree node read the named DependencyProperties and include in `NodeDto.Properties` dict. Apply redaction — redacted props return `"[REDACTED]"` inline.
8. **GC/detachment guard:** After resolving a nodeId to an object inside the Dispatcher block, verify connectivity. **Branch by type:**
   - For `Window` targets: use `window.IsInitialized` (NOT `PresentationSource.FromVisual` — that returns null for `Visibility.Hidden` windows, which are valid inspection targets). Reference: `SnoopWindowUtils.cs` and `WindowHelper.cs` in Snoop.Core.
   - For other `Visual` targets: use `PresentationSource.FromVisual(el) != null`
   - For non-`Visual` logical tree nodes (e.g., CLR data objects): skip the check — these don't have visual connectivity but are valid logical children
   If detached, throw `SnoopException(NodeNotFound, SnoopSuggestions.NodeNotFound)`.
   **Also:** `DtoProjection` must check `target is DependencyObject` before attempting DP property inspection. Non-`DependencyObject` nodes return CLR properties only via `TypeDescriptor.GetProperties()`, not DependencyProperty info.
9. **Null target guard:** If `rootTarget` is null or `TreeService.Construct()` returns null, throw `SnoopException(SessionNotFound, "Application not yet initialized")`.
10. **Timeout distinction:** `DispatcherBusy` = Dispatcher won't accept work within 500ms threshold. `OperationTimedOut` = Dispatcher accepted work but the operation itself exceeds `TimeoutMs`. Use two-phase timeout: try `Dispatcher.InvokeAsync` with 500ms for acceptance; once accepted, overall deadline is `TimeoutMs`.
11. **Redaction on binding info:** In `GetBindingInfoAsync`, if the property being inspected is redacted, return `[REDACTED]` for `BindingInfoDto.Path` and `BindingInfoDto.ResolvedValue`. Other binding metadata (mode, status, etc.) is OK to return.
12. Implement methods: `GetSessionInfoAsync`, `GetWindowsAsync`, `GetVisualTreeAsync`, `GetChildrenAsync`, `InspectElementAsync`, `GetPropertiesAsync`, `GetBindingInfoAsync`
13. Stub remaining methods (`GetAncestorsAsync`, `FindElementsAsync`, `SetPropertyAsync`, `RunDiagnosticsAsync`, `GetResourcesAsync`, `CaptureScreenshotAsync`, `GetTriggersAsync`, `GetBehaviorsAsync`) with `throw new NotImplementedException()` — filled in later beads (8 stubs)

**Acceptance Criteria:**
- [ ] All methods marshal via single `Dispatcher.InvokeAsync(() => { ... }, DispatcherPriority.Send)` — synchronous block inside lambda
- [ ] PropertyInformation: construct → read → project → Teardown all in ONE synchronous block (no interleaving)
- [ ] `includeProperties`: reads named DPs, includes in NodeDto.Properties, redacted props return `[REDACTED]`
- [ ] GC/detachment guard: detached elements throw `NodeNotFound`
- [ ] Null target: throws `SessionNotFound`
- [ ] Timeout: two-phase — `DispatcherBusy` (acceptance timeout) vs `OperationTimedOut` (execution timeout)
- [ ] Redacted properties: getter NOT invoked, returns `[REDACTED]`
- [ ] Binding info redaction: redacted property → `[REDACTED]` for Path and ResolvedValue
- [ ] Path alias resolution works (`Window\Grid\Button` → nodeId)
- [ ] Properties sorted by name
- [ ] `wpf_get_children` root ordering when nodeId omitted: main window first, then by title
- [ ] `SnoopInspector` implements `IDisposable` — disposes NodeRegistry (cancels timer), CursorManager (cancels timer), sets `_disposed` flag
- [ ] 7 methods fully implemented, 8 stubbed
- [ ] `dotnet build Snoop.sln` passes

---

### BEAD-003b: Engine — diagnostics, resources, screenshots

**PRD ref:** US-003b
**Depends on:** BEAD-003-inspector
**Blocks:** BEAD-018-combined
**Estimated:** 5 new files + 1 modify, ~300 LOC

**Files to create:**
- `SnoopWPF.Agent.Engine/DiagnosticsInspector.cs`
- `SnoopWPF.Agent.Engine/ResourceInspector.cs`
- `SnoopWPF.Agent.Engine/ScreenshotCapture.cs`
- `SnoopWPF.Agent.Tests/DiagnosticsInspectorTests.cs`
- `SnoopWPF.Agent.Tests/ResourceInspectorTests.cs`

**Files to modify:**
- `SnoopWPF.Agent.Engine/SnoopInspector.cs` — replace stubs for `RunDiagnosticsAsync`, `GetResourcesAsync`, `CaptureScreenshotAsync`

**Steps:**
1. `DiagnosticsInspector`: delegates to `DiagnosticContext` with the 5 active providers: `FreezeFreezables`, `LocalResourceDefinitions`, `NonVirtualizedLists`, `UnresolvedDynamicResource`, `BindingLeak`. (6th provider `BindingDiagnosticProvider` is commented out — leave disabled.) Subtree-scoped via root nodeId, cursor-paginated.
2. `ResourceInspector`: walk up tree from element, collect ResourceDictionary entries with origin labels. Precedence: effective (closest scope) first, shadowed included with their origin.
3. `ScreenshotCapture.CaptureAsPng(Visual visual)`: call `VisualCaptureUtil.RenderVisualWithHighQuality(visual, dpiX: 96, dpiY: 96)` (`int` parameters) → `RenderTargetBitmap` → `PngBitmapEncoder` → `MemoryStream`. Return `ms.ToArray()`. **No temp files.**
4. Wire into `SnoopInspector`. **All three helpers are called from within SnoopInspector's Dispatcher.InvokeAsync block — they must NOT create their own Dispatcher calls or async entry points.**
5. Unit tests

**Acceptance Criteria:**
- [ ] Diagnostics delegates to DiagnosticContext with all 5 providers
- [ ] Subtree-scoped diagnostics via root nodeId
- [ ] Diagnostics cursor-paginated (`CursorPage<DiagnosticItemDto>`)
- [ ] Resources: effective (closest) first, shadowed included with origin
- [ ] Screenshot: `byte[]` PNG in memory via `RenderVisualWithHighQuality` + `PngBitmapEncoder`, no temp files
- [ ] All unit tests pass

---

### BEAD-003c: Engine — triggers and behaviors

**PRD ref:** US-003c
**Depends on:** BEAD-003-inspector
**Blocks:** BEAD-018-combined (triggers/behaviors tested there)
**Estimated:** 4 new files + 1 modify, ~200 LOC

**Files to create:**
- `SnoopWPF.Agent.Engine/TriggerInspector.cs`
- `SnoopWPF.Agent.Engine/BehaviorInspector.cs`
- `SnoopWPF.Agent.Tests/TriggerInspectorTests.cs`
- `SnoopWPF.Agent.Tests/BehaviorInspectorTests.cs`

**Steps:**
1. `TriggerInspector`: Use `TriggerItemFactory.GetTriggerItem(triggerBase, depObj, triggerSource)` to create instances. `TriggerSource` enum values: `Style`, `ControlTemplate`, `DataTemplate`, `Element` (NOT bare "Template"). Walk the element's `Style?.Triggers`, `Template?.Triggers`, and local `Triggers` collection. Project to `TriggerDto[]`. Call `Dispose()` on each `TriggerItemBase` after projection. **Must be called from within Dispatcher.InvokeAsync block.**
2. `BehaviorInspector`: Reference `Snoop.Core/Views/BehaviorsTab/BehaviorsView.xaml.cs` — the `AddBehaviorsFromType` method (lines ~111–143) contains the working reflection pattern for both `System.Windows.Interactivity` and `Microsoft.Xaml.Behaviors`. The call site is in `UpdateBehaviorList` at ~line 106. Extract the reflection logic rather than reimplementing. **Must be called from within Dispatcher.InvokeAsync block.**
3. Handle elements with no triggers/behaviors gracefully (return empty arrays)
4. Wire into `SnoopInspector`
5. Unit tests

**Acceptance Criteria:**
- [ ] TriggerInspector works for Style, ControlTemplate, DataTemplate, and Element triggers
- [ ] BehaviorInspector works with both Interactivity libraries (reflection-based)
- [ ] Both inspectors designed to be called from within a Dispatcher.Invoke block (no own Dispatcher calls)
- [ ] Graceful empty results for elements without triggers/behaviors
- [ ] Unit tests pass

---

### BEAD-003d: SnoopManager — headless agent support

**PRD ref:** US-003d
**Depends on:** BEAD-001
**Blocks:** BEAD-022, BEAD-023
**Estimated:** 1 new file + 2 modifies, ~100 LOC (but SnoopManager.cs is complex — read carefully)

**Files to create:**
- `Snoop.Core/Infrastructure/IInjectedAgent.cs`

**Files to modify:**
- `Snoop.Core/Infrastructure/SnoopManager.cs` (~500 lines — read fully before modifying)
- `Snoop.Core/Data/TransientSettingsData.cs` (~92 lines)

**Steps:**
1. Create `IInjectedAgent` interface: `void Start(TransientSettingsData settings)`, `void Stop()`
2. Add `SnoopStartTarget.HeadlessAgent` enum value
3. Add to `TransientSettingsData`: `public string? PipeName { get; set; }` and `public string? SessionToken { get; set; }` (nullable strings — XmlSerializer handles these cleanly across net462 and net6+)
4. Add `SnoopManager.HeadlessAgentFactory` static property: `public static Func<IInjectedAgent>? HeadlessAgentFactory { get; set; }` — **this is for NuGet/in-process mode only**, where `SnoopAgent.Start()` sets the factory before calling `SnoopManager`. In injection mode, `SnoopAgentEntryPoint.Start()` bypasses `SnoopManager` entirely and creates `SnoopInspector` directly.
5. Create `InjectAgentIntoDispatchers` overload for `IInjectedAgent` (forked from existing Window-based path) — used by NuGet mode
6. In headless mode: skip `MessageBox.Show()`, set `MultipleDispatcherMode` → `AlwaysUse`
7. Replace `ErrorDialog.ShowDialog()` → `Trace.TraceError()` in headless path
7b. **Sanitize existing log statements in `TransientSettingsData`:** `WriteToFile()` (line ~40) logs `"Writing transient settings file to \"{settingsFile}\""` and `LoadCurrent()` logs similarly — both leak the settings file path. Remove or redact these log calls.
8. **Settings file security:** On net462: create file, then `File.SetAccessControl(path, fileSecurity)` (BCL). On net6+: add `System.IO.FileSystem.AccessControl` package; create file, then use extension method `new FileInfo(path).SetAccessControl(fileSecurity)` (note: `FileStream` constructor does NOT accept `FileSecurity` on net6+). Both: `FileSecurity` with inherited ACEs removed, add `FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FullControl, Allow)`.
9. **Log sanitization:** InjectorLauncher must NOT log `TransientSettingsData` file path or session token. Property values never logged. Exception payloads sanitized before logging (strip paths and tokens).
10. **SnoopLog.txt hardening:** Ensure `SnoopLog.txt` is created with owner-only DACL (same approach as settings file). Implement per-session rotation (delete/recreate on each injection).
11. Existing GUI path completely untouched

**Acceptance Criteria:**
- [ ] `IInjectedAgent` interface created
- [ ] `TransientSettingsData` has `PipeName` + `SessionToken` (nullable strings)
- [ ] `SnoopManager.HeadlessAgentFactory` static property
- [ ] MessageBox/ErrorDialog suppressed in headless mode
- [ ] Settings file written with owner-only DACL
- [ ] Log sanitization: no session tokens, property values, or pipe names in logs
- [ ] Exception payloads sanitized before logging
- [ ] `SnoopLog.txt`: owner-only DACL + per-session rotation
- [ ] Unit test: `SnoopWPF.Agent.Tests/LogSanitizationTests.cs` — inject known token + pipe name, capture log output, assert neither appears
- [ ] Existing GUI unaffected
- [ ] `dotnet test Snoop.Core.Tests` passes

---

### BEAD-003e: Engine — multi-dispatcher support (OPTIONAL for MVP)

**PRD ref:** US-003e
**Depends on:** BEAD-003-inspector
**Note:** OPTIONAL — skip unless target app has multiple dispatchers (~5% of WPF apps)

**Acceptance Criteria:**
- [ ] Per-dispatcher `SnoopInspector` instances with independent work queues
- [ ] Node IDs: `{dispatcherIdx}:{counter}`
- [ ] Dispatcher routing from nodeId prefix
- [ ] `GetSessionInfoAsync` returns dispatcher list with window nodeIds per dispatcher

---

## Milestone 2: MVP Tools (NuGet Mode)

### BEAD-004a: SnoopWPF.Agent.Tools — MVP tool handlers (7 tools)

**PRD ref:** US-004 (part 1 of 2)
**Depends on:** BEAD-003-inspector
**Blocks:** BEAD-005
**Goal:** Create the shared MCP tool handler project + the 7 MVP tools.
**Estimated:** 9 tool/utility files + 7 test files + .csproj, ~450 LOC

**Files to create:**
- `SnoopWPF.Agent.Tools/SnoopWPF.Agent.Tools.csproj` (net8.0-windows, refs: Contracts + ModelContextProtocol)
- `SnoopWPF.Agent.Tools/SessionInfoTool.cs`
- `SnoopWPF.Agent.Tools/GetWindowsTool.cs`
- `SnoopWPF.Agent.Tools/GetVisualTreeTool.cs`
- `SnoopWPF.Agent.Tools/GetChildrenTool.cs`
- `SnoopWPF.Agent.Tools/InspectElementTool.cs`
- `SnoopWPF.Agent.Tools/GetPropertiesTool.cs`
- `SnoopWPF.Agent.Tools/GetBindingInfoTool.cs`
- `SnoopWPF.Agent.Tools/ErrorMapping.cs`
- `SnoopWPF.Agent.Tests/Tools/*Tests.cs` (7 test files)

**MCP SDK pattern** (canonical example for all tool handlers):
```csharp
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class SessionInfoTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_get_session_info",
                   Description = "Get session info: process name, PID, .NET version, dispatchers, capabilities")]
    public async Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct)
        => await inspector.GetSessionInfoAsync(ct);
}
```
Register via DI: `services.AddSingleton<ISnoopInspector>(inspector); builder.Services.AddMcpServerToolType<SessionInfoTool>();`

**Steps:**
1. Create .csproj (`<LangVersion>latest</LangVersion>`, add `ModelContextProtocol` to `Directory.packages.props` if not already from BEAD-000a)
2. Create `ErrorMapping.cs`: **Mechanism:** each tool handler wraps its `inspector.*Async()` call in a try/catch. On `SnoopException`, either (a) throw `new McpException(formattedMessage)` (the SDK forwards `McpException.Message` to the client), or (b) return `new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = errorJson }] }`. Option (a) is simpler. The formatted message should be: `"[{SCREAMING_SNAKE_CODE}] {message}\n\nSuggestion: {suggestion}"`. **Error code wire format:** `SnoopErrorCode.NodeNotFound` → `"NODE_NOT_FOUND"`. Use manual mapping dict or `Regex.Replace(code.ToString(), "([a-z])([A-Z])", "$1_$2").ToUpperInvariant()`.
3. Implement 7 MVP tool handlers following the canonical pattern above
4. **Special case: `CaptureScreenshotTool`** (in BEAD-004b) — `ISnoopInspector.CaptureScreenshotAsync` returns `ScreenshotResultDto` (metadata + byte[]). The tool handler must convert this to a multi-block MCP response: `[TextContent(metadata JSON), ImageContent(base64 PNG)]`. This requires using the MCP SDK's content block API (verify exact API in BEAD-000a spike). Other tools just return DTOs directly.
5. Each tool has full `[Description]` attribute for MCP schema
6. Unit tests: each handler with mock `ISnoopInspector`
7. **`GetWindowsAsync` default:** The tool handler supplies `includeHidden: false` as the default when the MCP parameter is omitted.

**Acceptance Criteria:**
- [ ] 7 tool handlers as `[McpServerToolType]` classes
- [ ] All operate against `ISnoopInspector` interface
- [ ] `ErrorMapping`: SnoopException → MCP error with SCREAMING_SNAKE_CASE code + exact PRD suggestion text
- [ ] `includeHidden` defaults to `false` in `GetWindowsTool`
- [ ] All tool parameters have `[Description("...")]` attributes (from `System.ComponentModel`) for JSON Schema field docs — Claude needs these to understand each parameter
- [ ] Unit tests pass with mock inspector

---

### BEAD-004b: SnoopWPF.Agent.Tools — remaining 8 tool handlers

**PRD ref:** US-004 (part 2 of 2)
**Depends on:** BEAD-004a
**Goal:** Implement the remaining 8 tool handlers.
**Estimated:** 8 tool files + 8 test files, ~450 LOC

**Files to create:**
- `SnoopWPF.Agent.Tools/GetAncestorsTool.cs`
- `SnoopWPF.Agent.Tools/FindElementsTool.cs`
- `SnoopWPF.Agent.Tools/SetPropertyTool.cs`
- `SnoopWPF.Agent.Tools/RunDiagnosticsTool.cs`
- `SnoopWPF.Agent.Tools/GetResourcesTool.cs`
- `SnoopWPF.Agent.Tools/CaptureScreenshotTool.cs`
- `SnoopWPF.Agent.Tools/GetTriggersTool.cs`
- `SnoopWPF.Agent.Tools/GetBehaviorsTool.cs`
- `SnoopWPF.Agent.Tests/Tools/*Tests.cs` (8 test files)

**Acceptance Criteria:**
- [ ] All 8 handlers implemented following same pattern as BEAD-004a
- [ ] **EXCEPTION: `CaptureScreenshotTool`** must NOT return a DTO directly. It must return `CallToolResult` with two content blocks: `TextContentBlock` (metadata JSON) + `ImageContentBlock` (base64 PNG, mimeType "image/png"). This is the only tool that does not use the simple DTO-return pattern. Verify the exact SDK type names from BEAD-000a spike results.
- [ ] All tool parameters have `[Description("...")]` attributes (from `System.ComponentModel`) for JSON Schema field docs
- [ ] Unit tests pass with mock inspector
- [ ] All 15 tools total now implemented across 004a + 004b

---

### BEAD-005: SnoopWPF.Agent — NuGet MCP server package

**PRD ref:** US-005
**Depends on:** BEAD-004a (minimum), BEAD-004b (for full tool set)
**Blocks:** BEAD-006-012, BEAD-013
**Estimated:** 5 files + .csproj, ~250 LOC

**Files to create:**
- `SnoopWPF.Agent/SnoopWPF.Agent.csproj` (net8.0-windows)
- `SnoopWPF.Agent/SnoopAgent.cs`
- `SnoopWPF.Agent/SnoopAgentOptions.cs`
- `SnoopWPF.Agent/SnoopAgentHandle.cs`
- `SnoopWPF.Agent/McpServerSetup.cs`

**Steps:**
1. Create .csproj. References: Engine, Tools, Contracts, `ModelContextProtocol.AspNetCore`
2. `SnoopAgentOptions`: Port (default auto), EnableMutation (default false), EnableRedaction (default true), BearerToken (default null = random), LogLevel, TransportMode
3. `SnoopAgent.Start(Application, SnoopAgentOptions?)` → `SnoopAgentHandle`
4. `SnoopAgentHandle`: EndpointUri, BearerToken, `Stop()`
5. Reject second `Start()` call
6. Auto-stop on `Application.Exit`
7. Port-in-use: auto-fallback
8. `127.0.0.1` only — hardcoded
9. HTTP: bearer token required via `Authorization` header, CORS deny all, query-string tokens rejected
10. **Discovery file:** Create `%TEMP%\snoop-agent-{pid}.json` with owner-only DACL. Use `FileStream(path, FileMode.CreateNew)` then immediately `new FileInfo(path).SetAccessControl(fileSecurity)` (net6+ via `System.IO.FileSystem.AccessControl` extension method) or `File.SetAccessControl(path, fileSecurity)` (net462 BCL). Note: `FileStream` constructor does NOT accept `FileSecurity` on net6+. Write endpoint + token + pid as JSON. Delete in `SnoopAgentHandle.Stop()` and `Application.Exit`. Do NOT use `FileOptions.DeleteOnClose`.
11. Console output: `SnoopWPF.Agent MCP server listening at {uri}`
12. Register all tools from `SnoopWPF.Agent.Tools`
13. **JSON serialization policy:** Configure `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` for the MCP server's serializer. DTOs have PascalCase C# properties but must serialize as camelCase JSON (`hasMore`, `nodeId`, etc.) for AI agent consumption. Check if the MCP SDK exposes serializer options — if not, add `[JsonPropertyName]` attributes to DTOs (update BEAD-001 accordingly).

**Acceptance Criteria:**
- [ ] `SnoopAgent.Start()` returns `SnoopAgentHandle`
- [ ] Second `Start()` rejected
- [ ] Auto-stop on app exit
- [ ] Bearer token: required, header-only, CORS deny all, query-string rejected
- [ ] Bearer token negative tests: missing header → HTTP 401; wrong token → 401; token in query string → 400/401; OPTIONS preflight → no `Access-Control-Allow-Origin`
- [ ] Discovery file created atomically with owner-only DACL, deleted on Stop/Exit
- [ ] All 15 tools registered
- [ ] `dotnet build Snoop.sln` passes

---

### BEAD-006-012: MVP tool end-to-end verification

**PRD ref:** US-006–US-012
**Depends on:** BEAD-005
**Goal:** Verify each MVP tool works end-to-end against a real WPF app. Primarily test-writing.
**Estimated:** ~400 LOC total (test code)

**This is one combined bead.** Specific verification per tool:

| Tool | Verify |
|------|--------|
| `wpf_get_session_info` | Returns pid, dotnetVersion, dispatchers array with windowNodeIds, mutationEnabled |
| `wpf_get_windows` | Main window first; `includeHidden` default excludes hidden windows |
| `wpf_get_visual_tree` | maxDepth respected; 5000-node cap; `childrenTruncated` on cut nodes; `includeProperties` returns values; redacted props return `[REDACTED]` inline |
| `wpf_get_children` | Cursor pagination works; `stale: false` within 30s, `stale: true` after 30s+; root-level (no nodeId) returns main window first then by title |
| `wpf_inspect_element` | All summary fields populated; `triggerCount`/`behaviorCount` are null (not evaluated) |
| `wpf_get_properties` | Filter (case-insensitive substring on property name); category filter; sorted by name; `isRedacted` on sensitive props; cursor pagination |
| `wpf_get_binding_info` | `dataContextIsNull`, `dataContextType`, `resolvedValue`, binding error details |

**Acceptance Criteria:**
- [ ] Each tool returns expected data shape (see table above)
- [ ] At least one happy-path assertion per tool added to `SnoopWPF.Agent.Tests` (not deferred — must assert concrete values, not just compile)

---

### BEAD-013: SnoopWPF.Agent.SampleApp

**PRD ref:** US-013
**Depends on:** BEAD-005
**Blocks:** BEAD-014, BEAD-026
**Estimated:** 5 files + .csproj, ~200 LOC

**Steps:**
1. Create .NET 8 WPF app with:
   - Main window + nested layout (Grid → StackPanel → controls)
   - Bound TextBlock + ListBox with items
   - **Intentional binding error** (bad path on a TextBlock.Text binding)
   - **Non-virtualized ListBox** (for NonVirtualizedLists diagnostic)
   - **DataTrigger** on a control
   - **PasswordBox** (for redaction testing — password property must be redacted)
   - **Hidden window** (`Visibility.Hidden`) — for `includeHidden` and screenshot edge cases
   - **Resource shadowing** — define a resource at App level and override at Window level (for precedence testing)
   - **Unresolved DynamicResource** reference (for UnresolvedDynamicResource diagnostic)
2. Add `<PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" />` (add to `Directory.packages.props` with version). Include at least one behavior (e.g., `EventTriggerBehavior`).
3. Call `SnoopAgent.Start()` on startup
4. `--no-agent` command-line flag disables agent (for injection-mode testing)
5. Log endpoint URI to console. **Do NOT log the bearer token** — tests read it from the discovery file or `SnoopAgentHandle.BearerToken` in-process.

**Acceptance Criteria:**
- [ ] App launches with main window
- [ ] Contains: binding error, non-virtualized list, triggers, behaviors, PasswordBox, hidden window, resource shadowing, unresolved DynamicResource
- [ ] `SnoopAgent.Start()` activates MCP server
- [ ] `--no-agent` flag skips agent
- [ ] Discovery file written (tests read token from there)
- [ ] Bearer token NOT logged to console

---

### BEAD-014: Integration test harness — MVP

**PRD ref:** US-014
**Depends on:** BEAD-013, BEAD-006-012
**Estimated:** 8 files + .csproj, ~500 LOC

**Files to create:**
- `SnoopWPF.Agent.IntegrationTests/SnoopWPF.Agent.IntegrationTests.csproj`
- `SnoopWPF.Agent.IntegrationTests/SampleAppFixture.cs`
- `SnoopWPF.Agent.IntegrationTests/SessionInfoTests.cs`
- `SnoopWPF.Agent.IntegrationTests/TreeTests.cs`
- `SnoopWPF.Agent.IntegrationTests/PropertyTests.cs`
- `SnoopWPF.Agent.IntegrationTests/BindingTests.cs`
- `SnoopWPF.Agent.IntegrationTests/ErrorTests.cs`
- `SnoopWPF.Agent.IntegrationTests/ConcurrencyTests.cs`
- `SnoopWPF.Agent.IntegrationTests/SerializationRoundTripTests.cs`

**Steps:**
1. `SampleAppFixture`: launch sample app as process, **poll for discovery file with 15s deadline** (100ms intervals — the file may not exist until Kestrel binds), read token, create MCP HTTP client with bearer token. Use assembly-level `[SetUpFixture]` (one shared process for all tests). Mark assembly `[NonParallelizable]`. Do NOT use `[assembly: Apartment(STA)]` — tests must run on non-Dispatcher threads (per BEAD-003-inspector threading constraint). Configure `ProcessStartInfo` with `RedirectStandardInput/Output/Error = true`, `UseShellExecute = false`.
2. Tests for all MVP tools (session, windows, tree, children, inspect, properties, binding)
3. JSON schema validation
4. Concurrent request test (no deadlocks)
5. Error scenarios: invalid nodeId → `NODE_NOT_FOUND` + suggestion, timeout
6. **DTO serialization round-trip tests:** every DTO type through both `System.Text.Json` and `DataContractJsonSerializer` (from PRD testing plan)
7. 30s per test, 5min total

**Acceptance Criteria:**
- [ ] Sample app launches and connects via MCP
- [ ] All MVP tools tested end-to-end
- [ ] Concurrent requests don't deadlock
- [ ] Error scenarios: invalid nodeId → `NODE_NOT_FOUND` + suggestion; blocked Dispatcher → `DISPATCHER_BUSY` within TimeoutMs + 500ms
- [ ] DTO serialization round-trips pass (every DTO type through both `System.Text.Json` and `DataContractJsonSerializer`)
- [ ] Performance assertions (use `Stopwatch`, names suffixed `_PerformanceTarget`): `GetChildren` 100 children < 50ms, `GetProperties` ~80 props < 100ms, MCP startup < 200ms, idle memory < 20MB. **CI tolerance:** Run one warm-up call before timing. If `CI` env var is set, multiply thresholds by 5x (cold JIT on CI runners is 3-10x slower). Use `[Category("Performance")]` so CI can run them separately.
- [ ] `dotnet test SnoopWPF.Agent.IntegrationTests` passes

---

## Milestone 3: Full NuGet Tool Set

> BEAD-015, BEAD-016, BEAD-017 are **SERIALIZED** (all modify `SnoopInspector.cs`).
> BEAD-018-combined and BEAD-020 are parallel with each other and with 015-017.

### BEAD-015: Tool — wpf_find_elements

**PRD ref:** US-015
**Depends on:** BEAD-003-inspector, BEAD-005
**Blocks:** BEAD-016
**Files to modify:** `SnoopWPF.Agent.Engine/SnoopInspector.cs`

**Acceptance Criteria:**
- [ ] Subtree scope via `rootNodeId`
- [ ] `propertyConditions` with `Equals`/`Contains` operators
- [ ] Type name: short ("Button") or full, case-insensitive
- [ ] `totalScanned`, `truncated` in output
- [ ] `maxResults` capped at 100
- [ ] Integration test added

---

### BEAD-016: Tool — wpf_get_ancestors

**PRD ref:** US-016
**Depends on:** BEAD-015 (serialized — same file)
**Blocks:** BEAD-017
**Files to modify:** `SnoopWPF.Agent.Engine/SnoopInspector.cs`

**Acceptance Criteria:**
- [ ] Parent first, root last
- [ ] Each ancestor includes `dataContextType`
- [ ] `maxLevels` parameter respected
- [ ] Integration test added

---

### BEAD-017: Tool — wpf_set_property

**PRD ref:** US-017
**Depends on:** BEAD-016 (serialized — same file)
**Files to create:** `SnoopWPF.Agent.Engine/TypeConverterTable.cs`
**Files to modify:** `SnoopWPF.Agent.Engine/SnoopInspector.cs`

**Steps:**
1. Hardcoded converter table — NEVER call `TypeDescriptor.GetConverter()`
2. Safe types: string, bool, int, double, float, decimal, long, Color, Thickness, GridLength, CornerRadius, FontWeight, FontStyle, Visibility, HorizontalAlignment, VerticalAlignment, TextAlignment, Point, Size, Rect, all enum types
3. Never settable: Uri, ImageSource, BitmapSource, FontFamily, Style, ControlTemplate, DataTemplate, Binding, Type, any UIElement subtype
4. For enum types: `Enum.Parse(propertyType, value, ignoreCase: true)` — culture-neutral by design
5. For numeric types: `type.Parse(value, CultureInfo.InvariantCulture)`
6. Errors: `MutationDisabled`, `PropertyReadOnly`, `UnsupportedPropertyType`, `TypeConversionFailed` (with `expectedFormat` hint), `PropertyRedacted`
7. Log mutations: nodeId + property name only (NOT before/after values)

**Acceptance Criteria:**
- [ ] Hardcoded converter table, never `TypeDescriptor.GetConverter()`
- [ ] `MutationDisabled` when opt-in flag is off
- [ ] Safe types accepted, unsafe rejected
- [ ] Enum.Parse with ignoreCase, numeric types with InvariantCulture
- [ ] Mutation logging: nodeId + property name only
- [ ] Integration test with mutation enabled

---

### BEAD-018-combined: Tools — diagnostics, resources, triggers, behaviors (integration tests)

**PRD ref:** US-018, US-019, US-021
**Depends on:** BEAD-003b, BEAD-003c, BEAD-005
**Goal:** Add integration tests for diagnostic, resource, trigger, and behavior tools.
**Estimated:** 1 test file, ~150 LOC

**Acceptance Criteria:**
- [ ] `wpf_run_diagnostics`: subtree scope, cursor pagination, sorted by level (Critical first), detects binding error + non-virtualized list in sample app, verifies all 5 active provider areas appear in `DiagnosticItemDto.Area` (FreezeFreezables, LocalResourceDefinitions, NonVirtualizedLists, UnresolvedDynamicResource, BindingLeak)
- [ ] Performance: `RunDiagnostics` on 1000-element tree < 2s (Stopwatch assertion)
- [ ] `wpf_get_resources`: precedence (effective first, shadowed included), `resourceKey` filter, cursor pagination
- [ ] `wpf_get_triggers`: detects DataTrigger in sample app; triggerType, isActive, source, conditions, setters
- [ ] `wpf_get_behaviors`: detects behavior in sample app; typeName, assemblyName, properties

---

### BEAD-020: Tool — wpf_capture_screenshot

**PRD ref:** US-020
**Depends on:** BEAD-003b, BEAD-005
**Estimated:** 1 test file, ~50 LOC

**Acceptance Criteria:**
- [ ] Multi-block response: TextContent (metadata) + ImageContent (base64 PNG)
- [ ] No temp files, no filePath
- [ ] Fallback: node → MainWindow → first visible window
- [ ] Zero-size elements → `ELEMENT_NOT_RENDERABLE` error
- [ ] Hidden windows handled
- [ ] Integration test verifies ImageContent block structure
- [ ] Performance: `CaptureScreenshot` < 500ms (Stopwatch assertion)

---

## Milestone 4: Injection Mode

### BEAD-022: SnoopWPF.Agent.Remote — pipe client proxy

**PRD ref:** US-022
**Depends on:** BEAD-001, BEAD-003d
**Blocks:** BEAD-024, BEAD-025
**Estimated:** 4 files + .csproj, ~300 LOC

**Files to create:**
- `SnoopWPF.Agent.Remote/SnoopWPF.Agent.Remote.csproj` (net8.0-windows)
- `SnoopWPF.Agent.Remote/PipeSnoopInspectorProxy.cs`
- `SnoopWPF.Agent.Remote/PipeConnection.cs` (manages the `NamedPipeServerStream` on host side)
- `SnoopWPF.Agent.Remote/FramedJsonTransport.cs`
- `SnoopWPF.Agent.Tests/PipeTransportTests.cs`

**Pipe topology clarification:** In injection mode, the HOST creates `NamedPipeServerStream` (owns the pipe). The INJECTED AGENT connects as `NamedPipeClientStream`. `PipeSnoopInspectorProxy` lives on the HOST side and reads/writes from the server stream. `PipeConnection.cs` wraps `NamedPipeServerStream` — the name reflects the host's connection to the pipe, not the Named Pipe "client" role.

**Steps:**
1. `PipeSnoopInspectorProxy` implements full `ISnoopInspector` — every method serializes to `PipeRequest`, sends over pipe, deserializes `PipeResponse`
2. Handshake: host sends `{sessionToken, protocolVersion}` first (host speaks first), agent responds with capabilities
3. `PipeCancel` frame for in-flight cancellation (CancellationToken triggers cancel frame)
4. Per-operation timeout
5. **Pipe protocol unit tests:** framing correctness, 10MB max frame enforcement, cancel frame processing, malformed message handling (from PRD testing plan). Use mock pipe pairs (`MemoryStream` or in-process `NamedPipeServerStream`/`NamedPipeClientStream`).

**Acceptance Criteria:**
- [ ] `PipeSnoopInspectorProxy` fully implements `ISnoopInspector`
- [ ] STJ deserialization of pipe responses uses `PropertyNameCaseInsensitive = true` (safety net for net462 DCJS casing)
- [ ] Handshake: host sends `HandshakeChallenge` (sessionToken + protocolVersion); agent responds `HandshakeResponse`
- [ ] Protocol version check: if agent responds with different `ProtocolVersion`, close connection with `ProtocolMismatch`
- [ ] `PipeConnection` validates connected client PID via `GetNamedPipeClientProcessId()` — rejects mismatches
- [ ] Cancel frame works (CancellationToken triggers PipeCancel)
- [ ] Per-operation timeout
- [ ] **After pipe disconnect:** all subsequent `ISnoopInspector` method calls throw `SnoopException(SessionNotFound)` — not raw `IOException`
- [ ] Pipe protocol unit tests: framing correctness, 10MB max frame enforcement, cancel frame, malformed message → connection closed with `ProtocolMismatch` error (not crash/hang), version mismatch rejection
- [ ] Unit tests pass with mock pipe pairs

---

### BEAD-023: SnoopWPF.Agent.Injection — injected agent DLL

**PRD ref:** US-023
**Depends on:** BEAD-001, BEAD-003-inspector, BEAD-003d
**Blocks:** BEAD-024
**Estimated:** 4 files + .csproj, ~400 LOC

**Files to create:**
- `SnoopWPF.Agent.Injection/SnoopWPF.Agent.Injection.csproj` (net462;net6.0-windows, UseWpf=true)
- `SnoopWPF.Agent.Injection/SnoopAgentEntryPoint.cs`
- `SnoopWPF.Agent.Injection/PipeAgentServer.cs` (connects to host pipe as NamedPipeClientStream)
- `SnoopWPF.Agent.Injection/JsonSerializer.cs`

**CRITICAL: Entry point signature must be `public static int Start(string settingsFile)`** — the return type MUST be `int` (maps to HRESULT in `ExecuteInDefaultAppDomain` called by `Snoop.GenericInjector/Executor.cpp`). Return `0` on success.

**Steps:**
1. `net462;net6.0-windows`, **ZERO external NuGet deps** (exception: `System.IO.Pipes.AccessControl` on net462). Note: `[DataContract]`/`[DataMember]` attributes from `System.Runtime.Serialization.Primitives` are part of netstandard2.0 BCL — not an external dep.
1b. **Assembly resolution (CRITICAL for net462):** The very first thing `Start()` must do — BEFORE referencing any Engine/Contracts types — is install `AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => { /* resolve from Assembly.GetExecutingAssembly().Location directory */ }`. This ensures `SnoopWPF.Agent.Engine.dll`, `SnoopWPF.Agent.Contracts.dll`, and `Snoop.Core.dll` are found when the CLR tries to load them. All dependency DLLs must be deployed alongside `SnoopWPF.Agent.Injection.dll`. On net6+, `deps.json` handles this automatically.
2. JSON serialization: net462 → `DataContractJsonSerializer` (DTOs have `[DataContract]`/`[DataMember(Name="camelCase")]` so DCJS produces camelCase), net6+ → `System.Text.Json` with `PropertyNamingPolicy = CamelCase`
3. Custom framed protocol: `{4-byte LE length}{UTF-8 JSON}`
4. Connects to host pipe as `NamedPipeClientStream`. Validates session token from handshake.
5. Settings file: read → extract token → delete file → zero token from memory after handshake. **Token zeroing technique:** store token as `byte[]` (not `string` — strings are immutable in .NET). After handshake validation: `Array.Clear(tokenBytes, 0, tokenBytes.Length); tokenBytes = null;`. This actually overwrites memory unlike setting a `string` to null.
6. **Pipe method dispatch table:** `PipeAgentServer` reads `PipeRequest.Method` (string, e.g., `"GetChildren"`) and routes to the corresponding `ISnoopInspector` method on the local `SnoopInspector` instance. Implement as a `Dictionary<string, Func<string, CancellationToken, Task<string>>>` mapping method names to handlers that deserialize `ParamsJson`, call the inspector, and serialize the result to `ResultJson`. All 15 methods must be in the dispatch table.
7. Graceful cleanup on disconnect

**Acceptance Criteria:**
- [ ] Entry point: `public static int Start(string settingsFile)` returns int
- [ ] `net462;net6.0-windows`, zero external NuGet deps
- [ ] Framed JSON protocol (no StreamJsonRpc)
- [ ] net462: DataContractJsonSerializer, net6+: STJ
- [ ] Session token: file deleted, memory zeroed (byte[] cleared with `Array.Clear`, not string nullification)
- [ ] All ISnoopInspector methods available over pipe
- [ ] Unit test `SnoopWPF.Agent.Tests/InjectedAgentEntryPointTests.cs`: after `Start()`, settings file no longer exists; token byte array is cleared

---

### BEAD-024: SnoopWPF.Agent.Host — injection-mode MCP host (`snoop-mcp.exe`)

**PRD ref:** US-024
**Depends on:** BEAD-002, BEAD-004b (needs all 15 tools), BEAD-022, BEAD-023
**Estimated:** 2 files + .csproj, ~150 LOC

**Files to create:**
- `SnoopWPF.Agent.Host/SnoopWPF.Agent.Host.csproj` (net8.0-windows)
- `SnoopWPF.Agent.Host/Program.cs`

**Steps:**
1. CLI: `snoop-mcp --pid <pid> [--port <port>] [--enable-mutation]`
2. Default: stdio MCP transport. `--http`: HTTP/SSE with bearer token.
3. Uses `Snoop.Injector` for injection. **CRITICAL wiring:** Add a new `ProcessInfo.InjectAgent(...)` method that calls `InjectorLauncherManager.Launch` with string constants: assembly `"SnoopWPF.Agent.Injection"`, class `"SnoopWPF.Agent.Injection.SnoopAgentEntryPoint"`, method `"Start"`, and the settings file path. This mirrors `ProcessInfo.Snoop()` but targets the agent entry point instead of `SnoopManager.StartSnoop`. **Do NOT set `SnoopManager.HeadlessAgentFactory`** — that mechanism is for NuGet/in-process mode only. In injection mode, `SnoopAgentEntryPoint.Start()` creates its own `SnoopInspector` directly without going through `SnoopManager`.
4. Uses `PipeSnoopInspectorProxy` for RPC
5. Uses `SnoopWPF.Agent.Tools` for MCP tool handlers (same 15 tools as NuGet mode)
6. Pipe: random GUID name + user ACL + 10s connection timeout + client PID verification via `GetNamedPipeClientProcessId()`
7. **Minimal process handle lifetime:** close `PROCESS_ALL_ACCESS` handle immediately after injection
8. Bundle InjectorLauncher binaries as content files with `CopyToOutputDirectory = PreserveNewest` — path must be `{OutputDir}/Snoop.InjectorLauncher.{arch}.exe` (matches `InjectorLauncherManager` resolution pattern)
9. Graceful shutdown on target exit / Ctrl+C

**Acceptance Criteria:**
- [ ] `snoop-mcp --pid <pid>` injects and exposes MCP over stdio
- [ ] `--http` flag enables HTTP/SSE with bearer token, CORS deny-all, query-string token rejection (same rules as BEAD-005)
- [ ] Same 15 tools as NuGet mode
- [ ] Pipe security: random GUID, user ACL, session token, PID verification via `GetNamedPipeClientProcessId()`
- [ ] Process handle closed immediately after injection
- [ ] InjectorLauncher binaries as content files
- [ ] Graceful shutdown

---

### BEAD-025: SnoopWPF.Agent.CLI — CLI tool (`snoop-cli.exe`)

**PRD ref:** US-025
**Depends on:** BEAD-002, BEAD-022
**Estimated:** 3+ files + .csproj, ~400 LOC (borderline — can stub table format if context runs low)

**Steps:**
1. `snoop-cli.exe`, net8.0-windows. Dependencies: `Snoop.Injector`, `SnoopWPF.Agent.Remote`, `SnoopWPF.Agent.Contracts`, `System.CommandLine`, `Spectre.Console`
2. Stateless: each command auto-injects, queries, disconnects
3. Commands: `session`, `windows`, `tree`, `children`, `ancestors`, `find`, `inspect`, `properties`, `set-property`, `binding`, `diagnostics`, `resources`, `screenshot`, `triggers`, `behaviors`
4. Global: `--pid <pid>`, `--format json|table|tree`, `--port <port>` (connect to existing MCP server — skip injection), `--token <bearer>` (for authenticated HTTP connection; if omitted with `--port`, reads from discovery file `%TEMP%\snoop-agent-{pid}.json`)
5. `--port` takes precedence over `--pid`. When using `--port`, the CLI acts as an MCP HTTP client (not pipe client).
6. `--format table` via Spectre.Console; `--format json` outputs raw JSON
7. Exit code 0/non-zero

**Acceptance Criteria:**
- [ ] All 15 commands implemented
- [ ] `--pid` vs `--port` routing correct
- [ ] `--format json` works for all commands
- [ ] `--format table` renders via Spectre.Console
- [ ] Exit codes correct

---

### BEAD-026: Injection mode integration tests

**PRD ref:** US-026
**Depends on:** BEAD-024, BEAD-013
**Estimated:** 1 file, ~100 LOC

**Steps:**
1. Launch sample app with `--no-agent`
2. Run `snoop-mcp --pid <pid>` as process
3. Connect via MCP stdio
4. Exercise: get_session_info (verify PID matches sample app), get_windows, get_visual_tree, get_properties, get_binding_info
5. Error test: invalid nodeId over pipe → `NODE_NOT_FOUND`
6. Verify injection into net8.0 target works

**CI Note:** `CreateRemoteThread`-based injection may be blocked by Windows Defender on GitHub Actions hosted runners. Add `[Category("RequiresInjection")]` to these tests. In CI workflow, add `Add-MpPreference -ExclusionPath $env:GITHUB_WORKSPACE` before running injection tests, or skip them in CI and require local-only execution.

**Acceptance Criteria:**
- [ ] Sample app launches with `--no-agent`
- [ ] Injection via snoop-mcp succeeds (locally; may require AV exclusion in CI)
- [ ] Core tools return correct data
- [ ] `ProcessStartInfo` for snoop-mcp: `RedirectStandardInput/Output/Error = true`, `UseShellExecute = false`

---

## Milestone 5: Release

### BEAD-027: Build system and CI

**PRD ref:** US-027
**Depends on:** BEAD-015, BEAD-016, BEAD-017, BEAD-018-combined, BEAD-020 (all M3), BEAD-026
**Estimated:** 3 files modified, ~100 LOC

**Steps:**
1. Verify all projects in `Snoop.sln`
2. Update Nuke build file at **`.build/Build.cs`** — add targets for Agent tests, integration tests, NuGet pack. Run `dotnet run --project .build -- --help` to see existing targets first.
3. GitHub Actions: build + test on `windows-latest`
4. NuGet package `SnoopWPF.Agent` with metadata (MS-PL, description, tags, icon). **Pack strategy:** Set `<IsPackable>false</IsPackable>` on Engine, Tools, and Contracts projects. In `SnoopWPF.Agent.csproj`, add `<PrivateAssets>all</PrivateAssets>` to each `<ProjectReference>` so their DLLs are flattened into `lib/net8.0-windows/` of the package (not listed as separate package deps). Also set `<PackageId>SnoopWPF.Agent</PackageId>` explicitly. `snoop-mcp.exe` CANNOT be `PublishSingleFile` — InjectorLauncher EXEs must be alongside.
5. `snoop-mcp.exe` and `snoop-cli.exe` as artifacts
6. `LangVersion` set to `latest` in new projects
7. NuGet lock files: add `<RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>` to `Directory.build.props` (note: lowercase 'b' — matches existing file on disk). Run `dotnet restore Snoop.sln` to generate `packages.lock.json` for each new project. Commit lock files.

**Acceptance Criteria:**
- [ ] All projects in solution
- [ ] `.build/Build.cs` updated with new targets
- [ ] CI green on windows-latest
- [ ] NuGet package produced
- [ ] Executables as artifacts
- [ ] Lock files committed

---

### BEAD-028: Documentation, security, and log hardening

**PRD ref:** US-028
**Depends on:** BEAD-027
**Estimated:** 3 new doc files, ~200 LOC

**Steps:**
1. `README.md`: NuGet quick-start (3 lines), injection mode, Claude Code config for both transports
2. `SECURITY.md`: vulnerability disclosure, security contact, mutation/redaction docs, "reading properties executes target app getters" warning, TypeConverter whitelist, no method invocation in v1
3. `CHANGELOG.md`: 1.0.0 entry
4. NuGet metadata: MS-PL license, description, tags, icon
5. Verify `SnoopWPF.Agent` name available on nuget.org
6. Note: this PRD supersedes `TRANSFORMATION_PLAN.md`
7. **Log hardening:** Verify `SnoopLog.txt` ACL and rotation from BEAD-003d is working correctly in all modes.

**Acceptance Criteria:**
- [ ] README: NuGet quick-start + injection mode + Claude Code config
- [ ] SECURITY.md: complete with all warnings
- [ ] CHANGELOG.md: 1.0.0 entry
- [ ] NuGet metadata complete
- [ ] SnoopLog.txt ACL and rotation verified

---

## Performance Validation (cross-cutting — run after Milestone 2)

> From PRD performance targets. Not a separate bead — add these assertions to integration tests as they are written.

| Metric | Target | Where to test |
|--------|--------|---------------|
| `GetChildren` (100 children) | < 50ms | BEAD-014 |
| `GetProperties` (~80 props) | < 100ms | BEAD-014 |
| `RunDiagnostics` (1000 elements) | < 2s | BEAD-018-combined |
| `CaptureScreenshot` | < 500ms | BEAD-020 |
| MCP server startup | < 200ms | BEAD-014 |
| Memory overhead (idle) | < 20MB | BEAD-014 |

---

## Bead Dependency Summary

```
Phase 0:
  BEAD-000a   BEAD-000b ──> BEAD-000c
  BEAD-000d

Milestone 1 (foundation):
  BEAD-001 ──┬──> BEAD-003-infra ──> BEAD-003-inspector
             │      ├──> BEAD-003b (diag/resources/screenshots)
             │      ├──> BEAD-003c (triggers/behaviors)
             │      └──> BEAD-003e (multi-dispatcher, OPTIONAL)
             └──> BEAD-003d (SnoopManager headless)
  BEAD-002 (Injector) — parallel with all of the above

Milestone 2 (MVP NuGet):
  BEAD-003-inspector ──> BEAD-004a (7 MVP tools) ──> BEAD-004b (8 more tools)
  BEAD-004a ──> BEAD-005 (NuGet server)
  BEAD-005 ──> BEAD-006-012 (tool verification)
  BEAD-005 ──> BEAD-013 (sample app)
  BEAD-013 + BEAD-006-012 ──> BEAD-014 (integration tests)

Milestone 3 (full tools — SERIALIZED for 015-017, rest parallel):
  BEAD-005 + BEAD-003-inspector ──> BEAD-015 ──> BEAD-016 ──> BEAD-017
  BEAD-003b + BEAD-005 ──> BEAD-018-combined
  BEAD-003b + BEAD-005 ──> BEAD-020
  [BEAD-003c + BEAD-005 ──> triggers/behaviors tested in BEAD-018-combined]

Milestone 4 (injection):
  BEAD-001 + BEAD-003d ──> BEAD-022 (Remote proxy)
  BEAD-001 + BEAD-003-inspector + BEAD-003d ──> BEAD-023 (Injection DLL)
  BEAD-002 + BEAD-004b + BEAD-022 + BEAD-023 ──> BEAD-024 (Host)
  BEAD-002 + BEAD-022 ──> BEAD-025 (CLI)
  BEAD-024 + BEAD-013 ──> BEAD-026 (Injection tests)

Milestone 5 (release):
  All M3 beads + BEAD-026 ──> BEAD-027 (Build/CI) ──> BEAD-028 (Docs)
```

**Critical path to MVP demo:**
`BEAD-000a (version pin) → BEAD-001 → BEAD-003-infra → BEAD-003-inspector → BEAD-004a → BEAD-005 → BEAD-013 → BEAD-014`

**Maximum parallelism:**
- Phase 0: 000a, 000b, 000d parallel (000c waits for 000b)
- BEAD-001 and BEAD-002: parallel
- BEAD-003b, 003c, 003d: parallel (all need only 003-inspector or 001)
- BEAD-004b and BEAD-006-012: parallel after BEAD-005
- BEAD-018-combined and BEAD-020: parallel
- BEAD-022 and BEAD-023: parallel
- BEAD-024 and BEAD-025: partially parallel (both need 022)

**Total bead count:** 30 beads (was 32 before merge/split optimization)

---

## Known PRD↔Beads Divergences

These intentional changes override the PRD. If agents encounter conflicts, the beads are authoritative:

| Item | PRD says | Beads say | Rationale |
|------|----------|-----------|-----------|
| Error code count | US-001: "all 10 codes" | 11 codes (includes `ElementNotRenderable`) | PRD error table lists 11; US-001 text has a typo |
| Reverse node-ID dict key | `Dictionary<int, WeakReference<object>>` | `Dictionary<string, WeakReference<object>>` | String key `"0:42"` supports multi-dispatcher from day one |
| Discovery file lifecycle | `FILE_FLAG_DELETE_ON_CLOSE` | Explicit delete in `Stop()` + `Application.Exit` | `DeleteOnClose` deletes on stream dispose — before consumer reads |
| Screenshot API | `VisualCaptureUtil.SaveVisual()` → byte[] | `RenderVisualWithHighQuality()` → `PngBitmapEncoder` | `SaveVisual` writes to disk; we need in-memory |
| TriggerDto.Source values | `"Style"\|"Template"\|"Element"` | `"Style"\|"ControlTemplate"\|"DataTemplate"\|"Element"` | Matches actual `TriggerSource` enum in Snoop.Core |
| Settings file DACL (net6+) | `FileStream` + `FileSecurity` constructor | `FileSystemAclExtensions.SetAccessControl()` | `FileStream` has no `FileSecurity` overload on net6+ |
| DTO serializer attributes | "NO serializer attributes" | `[DataContract]` + `[DataMember(Name="camelCase")]` on all DTOs | DCJS requires these for correct serialization; attrs are in netstandard2.0 BCL (zero external deps) |
| Dictionary in DTOs | `Dictionary<string,string>` | `List<NameValuePairDto>` | DCJS serializes Dictionary as array of {Key,Value}; STJ as object — incompatible |
| Pipe protocol types | Single `HandshakeMessage` | Split: `HandshakeChallenge` (host→agent) + `HandshakeResponse` (agent→host) | Asymmetric handshake needs distinct types |
| Sample app logging | Logs bearer token to console | Does NOT log token; tests read from discovery file | Logging tokens violates security model |
| SnoopManager.HeadlessAgentFactory scope | Used in injection mode | NuGet/in-process mode ONLY; injection bypasses SnoopManager | Cross-process static property setting is impossible |

## Notes for Agents

- **BEAD-000a output (MCP SDK version pin) is a soft prerequisite for BEAD-004a/005** — BEAD-001 can start in parallel with 000a, but 004a should not start until 000a has recorded the pinned SDK version and registration pattern.
- **If the MCP SDK `[McpServerToolType]` pattern or return types differ from the canonical example in BEAD-004a,** update all tool handlers to match the verified pattern. The spike (BEAD-000a) is authoritative for MCP SDK API shape.
- **`PropertyInformation.GetProperties()` may eagerly evaluate some property values during construction.** BEAD-000b (engine spike) must verify whether the redaction "getter NOT invoked" guarantee is achievable with this API. If not, the spike should document the limitation and BEAD-003-inspector must adapt (e.g., by filtering the property list before calling `GetProperties`, or by accepting that construction-time evaluation is unavoidable for certain properties).
- **`DataContractJsonSerializer` without `[DataContract]`/`[DataMember]` attributes:** BEAD-000d (pipe spike) must verify that plain POCO classes (no attributes, public settable properties) serialize correctly with `DataContractJsonSerializer` on net462. If not (DCJS defaults to opt-in with `[DataContract]`), the Contracts DTOs may need `[DataContract]`/`[DataMember]` attributes on a net462-conditional basis, or the injection agent must use `JavaScriptSerializer` or manual JSON on net462 instead.
- **BEAD-005 depends on "BEAD-004a (minimum)":** This means BEAD-005 registers whichever tools are available at build time. If only BEAD-004a is complete (7 tools), BEAD-005 registers 7. After BEAD-004b completes, BEAD-005 automatically picks up all 15 (they're discovered via `AddMcpServerToolType` calls or assembly scanning). The `dotnet build` gate passes with either 7 or 15 tools.
