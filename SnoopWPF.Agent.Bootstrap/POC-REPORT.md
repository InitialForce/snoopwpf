# POC-REPORT: hostfxr bootstrap WORKS on CoreCLR

**Bead:** bd-1a9.32 — WS2-05: injection split — bootstrap DLL (DllMain no-op) + agent DLL
**Date:** 2026-04-18
**Machine:** AMD Ryzen 9 5950X, 64 GB RAM, .NET SDK 10.0.201, VS 2022 v17.14

## Verdict

**hostfxr bootstrap WORKS on CoreCLR (net10.0-windows).**

Phase B2 (Mode 3 generic-WPF injection) is UNBLOCKED. Proceed with bd-1a9.33.

## Spike implementation

### Files created

| File | Purpose |
|------|---------|
| `SnoopWPF.Agent.Bootstrap/dllmain.cpp` | DllMain returns TRUE immediately — no managed work |
| `SnoopWPF.Agent.Bootstrap/SnoopAgentStart.cpp` | Worker-thread entry: hostfxr chain → managed entry |
| `SnoopWPF.Agent.Bootstrap/SnoopWPF.Agent.Bootstrap.vcxproj` | x64-only C++ DLL project |
| `SnoopWPF.Agent.Injection/SnoopAgentUnmanagedEntry.cs` | `[UnmanagedCallersOnly]` managed entry point |
| `SnoopWPF.Agent.Bootstrap.SmokeTest/` | In-proc LoadLibrary smoke test (net10.0-windows) |

### Build results

```
msbuild SnoopWPF.Agent.Bootstrap.vcxproj /p:Configuration=Debug /p:Platform=x64
→ Build succeeded. 0 errors, 0 warnings.

dotnet build SnoopWPF.Agent.Injection.csproj -f net8.0-windows10.0.19041.0
→ Build succeeded. 0 errors, 0 warnings.

dotnet build SnoopWPF.Agent.Bootstrap.SmokeTest.csproj
→ Build succeeded. 0 errors, 0 warnings.
```

## Smoke test output (verbatim)

```
[SmokeTest] SnoopWPF.Agent.Bootstrap hostfxr chain smoke test
[SmokeTest] Process TFM: net10.0-windows (CoreCLR)
[SmokeTest] PID: 85128
[SmokeTest] PASS: hostfxr.dll loaded at 0x7FF935440000
[SmokeTest] Loading bootstrap DLL: ...\SnoopWPF.Agent.Bootstrap.dll
[SmokeTest] PASS: bootstrap DLL loaded at 0x7FF8DB650000
[SmokeTest] PASS: SnoopAgentStart export at 0x7FF8DB654188
[SmokeTest] Calling SnoopAgentStart (smoke-test mode, param=null)...
[SnoopAgentUnmanagedEntry] Start called, settingsPath=''
[SnoopAgentUnmanagedEntry] Smoke-test mode — no settings file, returning 0.
[SmokeTest] SnoopAgentStart returned: 0x00000000 in 39ms
[SmokeTest] PASS: hostfxr bootstrap chain WORKS on CoreCLR (net10.0-windows).
[SmokeTest] Phase B2 (Mode 3 injection) is UNBLOCKED — proceed with bd-1a9.33.
```

## Path A chain validated (all steps green)

| Step | API | Result |
|------|-----|--------|
| 1 | `GetModuleHandleW("hostfxr.dll")` | PASS — always loaded in CoreCLR process |
| 2 | `GetProcAddress(hHostFxr, "hostfxr_initialize_for_runtime_config")` | PASS |
| 2 | `GetProcAddress(hHostFxr, "hostfxr_get_runtime_delegate")` | PASS |
| 3 | runtimeconfig.json derived from exe path | PASS — `<exeName>.runtimeconfig.json` found |
| 4 | `hostfxr_initialize_for_runtime_config()` | PASS — rc=0 (Success) |
| 5 | `hostfxr_get_runtime_delegate(hdt_load_assembly_and_get_function_pointer)` | PASS |
| 6 | `load_assembly_and_get_function_pointer(SnoopWPF.Agent.Injection.dll, ...)` | PASS |
| 7 | Managed `[UnmanagedCallersOnly] Start(nint, int)` invoked | PASS — returns 0 in 39ms |

## Key design decisions confirmed

### DllMain is a no-op
DllMain returns TRUE with no work. `SnoopAgentStart` is the real entry, called from a
worker thread by the injector (bd-1a9.33 via `CreateRemoteThread`). This satisfies the
loader-lock safety requirement.

### No `LoadLibrary("hostfxr.dll")` needed
In a running CoreCLR process, `hostfxr.dll` is ALWAYS already loaded. The bootstrap DLL
uses `GetModuleHandleW` to obtain the handle — no disk I/O, no path search.

### `hostfxr_initialize_for_runtime_config` creates a secondary context, not a second CLR
The function call creates an additional ALC-based host context inside the already-running
CoreCLR. It does NOT start a new runtime. This is the correct approach for in-process
hosting (the "additional host" use case documented at microsoft/dotnet-samples).

### runtimeconfig.json fallback
Primary: `<exe>.runtimeconfig.json` alongside the target exe.
Fallback: `SnoopWPF.Agent.Bootstrap.runtimeconfig.json` alongside the bootstrap DLL.
The fallback supports cases where the target runtimeconfig is inaccessible (single-file
publish, UWP, etc.) — in those cases a sidecar config specifying `net8.0-windows` is
emitted by the injector (bd-1a9.33).

### .NET Framework detection
If `hostfxr.dll` is not loaded, `SnoopAgentStart` returns `INJECT_WRONG_RUNTIME`
(0xE0010001) immediately without crashing. The injector (bd-1a9.33) surfaces this as a
clear error to the broker and the allowlist marks the target as incompatible with Mode 3.

### Managed entry uses `nint` not `char*`
`[UnmanagedCallersOnly]` with `nint` parameters avoids the `AllowUnsafeBlocks` requirement
on the Injection project. `Marshal.PtrToStringUni` converts the native pointer safely.

## Not tested in this spike (deferred to bd-1a9.33)

- Remote process injection via `CreateRemoteThread` + ASLR-correct RVA calculation
- Settings file path passed via shared memory or temp file
- Multi-process stability (50-iteration requirement from ACs)
- CI integration test against a live WPF target

## Recommendation for Phase B2

**PROCEED with bd-1a9.33 (Mode 3 remote injection).**

The hostfxr chain is validated. The injector (bd-1a9.33) only needs to:

1. Map `SnoopWPF.Agent.Bootstrap.dll` into the target process (standard `LoadLibraryW`
   via `CreateRemoteThread` — well-understood technique).
2. Resolve the RVA of `SnoopAgentStart` from the PE export table.
3. Call `CreateRemoteThread` with the absolute address and a pointer to the settings file
   path (injected as a remote buffer via `WriteProcessMemory`).
4. Wait for the remote thread exit code; interpret via the `INJECT_*` constants.

No redesign is needed for CoreCLR targets. The `ICorRuntimeHost` path in
`Snoop.GenericInjector` (existing C++) continues to serve .NET Framework 4.x targets.
