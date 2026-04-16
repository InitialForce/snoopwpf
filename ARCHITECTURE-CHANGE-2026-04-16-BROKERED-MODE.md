# ARCHITECTURE CHANGE NOTICE — Brokered integration mode added

**Date**: 2026-04-16
**Affects**: PRD-v5-MVP.md §4.1, §4.2, §4.3, §9.7. BEADS-MVP.md: beads
M1-01, M1-04, M1-16, M1-19, M2-13, M2-14, M2-19. Consumer-side PRD at
`/c/work/desktop/wpf-mcp/PRD-snoop-integration.md`.

**Read this before continuing any bead.** Then re-read PRD-v5-MVP.md
§4.1 and §4.2 for the normative architecture.

---

## What changed

Integration-mode count goes from 2 (Co-located, Injection) to **3**
(Co-located, **Brokered** new, Injection).

| Mode | Hosts MCP | Transport | Max tier | Use |
|------|-----------|-----------|----------|-----|
| Co-located | Target app | stdio | L1 | Single long-lived process (dev attaches Claude). |
| **Brokered** (new) | External broker | stdio-MCP + named-pipe | **L1** | **Owned apps with multi-instance lifecycle** (MC test runs). |
| Injection | External injector | stdio + pipe | L0 read-only | Third-party opaque apps. |

## Why

MCP clients (Claude Code, Cursor, Cline) launch a single stdio child
per `.mcp.json` entry and stay attached for the client's lifetime.
They cannot rediscover a new target process. Co-located mode therefore
ties the MCP anchor to the target process lifetime — which breaks
every test run that spawns a fresh target per scenario, every
crash-recovery scenario, and every multi-process lifecycle pattern.

MotionCatalyst (our primary reference consumer) needs to spawn fresh
MC instances per test scenario. Co-located mode is unusable for this.
Brokered mode decouples the MCP anchor (long-lived broker) from the
agent (ephemeral in target).

## API change (new public surface)

```csharp
// Existing (unchanged):
public static void StartCoLocated(Application app, SnoopAgentOptions opts);

// New:
public static void StartBrokered(Application app, string pipeName,
                                 SnoopAgentOptions opts);

// Existing (unchanged):
public static void StartInjection(...);  // runtime-loaded, L0 only
```

`StartBrokered` opens a `NamedPipeServerStream(pipeName, ..., maxAllowedInstances=1,
options=PipeOptions.Asynchronous)` and listens for a single broker
client. Same Dispatcher-marshalled tool execution as CoLocated, just
over a different transport.

## Session policy change

`SessionMode` enum:

```csharp
// Before:
public enum SessionMode { CoLocated = 0, Injection = 1 }

// After:
public enum SessionMode { CoLocated = 0, Brokered = 1, Injection = 2 }
```

`SessionPolicy.Create` has three mode-specific paths:

- `CoLocated` — caller opts pass through unchanged (unchanged
  behaviour).
- `Brokered` — caller opts pass through unchanged (new; behaves like
  CoLocated for policy purposes — owned app, caller owns redaction
  choice, full L1 available).
- `Injection` — **forces** `EnableRedaction = true` regardless of
  caller (MF-11, unchanged).

## Beads affected (action required)

### Update in place

- **M1-01 SessionPolicy + SessionMode + InputTier** — add `Brokered`
  to the enum, add `SessionPolicy.Create(Brokered, opts)` path.
  Acceptance: all three `SessionPolicy.Create` variants tested; MF-11
  redaction-forcing test still targets `Injection` only; new test
  confirms `Brokered` respects caller redaction.
- **M1-16 Roslyn analyzer SWPF0001 (no Console.Write* in MCP
  entrypoint)** — narrow scope: analyzer fires only in projects
  marked `[SnoopMcpEntrypoint]` AND using `StartCoLocated`.
  `StartBrokered` consumers (broker processes, not target processes)
  still own their own stdout and need the analyzer; target processes
  under `StartBrokered` do NOT need the analyzer. Split the marker
  attribute if needed: `[CoLocatedEntrypoint]` vs
  `[BrokeredEntrypoint]`, or use a single attribute with a mode
  parameter.
- **M1-19 Console.Out takeover in StartCoLocated** — still valid as
  written (applies only to `StartCoLocated`). Add a note: `StartBrokered`
  does NOT take over stdout because the target owns its own stdio;
  the broker (separate process) owns the MCP stdio anchor.

### Scope down or remove

- **M2-13 SnoopWPF.Agent.Shim.FlaUI scaffolding** — **REMOVE**. The
  shim moves to the MC repo as `UiMcpActionCatalog` in
  `MotionCatalyst.Test.UI.Core`. Snoopwpf exposes the tool surface
  via MCP; MC adapts it to `IUIActionCatalog` in its own test
  project. Rationale: snoopwpf should not couple to MC's test-
  interface names.
- **M2-14 SnoopWPF.Agent.Shim.FlaUI IUIActionCatalog impl** —
  **REMOVE**. Same reason.

### Rewrite

- **M2-19 MotionCatalyst integration** — replace the content with
  "see `/c/work/desktop/wpf-mcp/PRD-snoop-integration.md`". MC-side
  work is a separate engineering stream with its own PRD and its own
  bead list (`BEADS-MC-MVP.md` to be written on MC side). Snoopwpf
  bead should cover only the SNOOPWPF-side changes that MC needs:
  the `StartBrokered` API (new bead M1-21), the broker scaffolding
  in `SnoopWPF.Agent.Host` / `.Remote` packages (new bead M2-21 or
  moved into existing Host project), and publication of pre-release
  packages for MC to consume.

### Clarify

- **M1-04 MF-11 injection-mode forced redaction** — wording update:
  "forced true in Injection mode **only**; CoLocated and Brokered
  both pass through the caller's choice." No behavioural change; just
  doc-level clarity now that there are three modes.

## New beads (add)

- **M1-21 `SnoopAgent.StartBrokered(app, pipeName, opts)` API** —
  implement the brokered startup path: open
  `NamedPipeServerStream`, install same Dispatcher-marshalled tool
  execution as CoLocated, clean teardown on `SnoopAgent.Stop()`.
  Unit test: a single mock broker client connects, sends a framed
  request, receives a framed response. Integration test: like the
  existing CoLocated smoke test but with a pipe-based client instead
  of the in-process MCP server loopback.
- **M1-22 Brokered-mode pipe framing** — frame format, request-ID
  correlation, out-of-order tolerance (or explicit in-order guarantee
  — pick one; CoLocated MCP is in-order so in-order is the low-risk
  choice). Reuse the `SnoopWPF.Agent.Remote` framing from v3 if it
  already provides this.
- **M2-21 `SnoopWPF.Agent.Host` broker scaffolding** — generic
  pieces only: MCP stdio server, pipe client router, 18-tool proxy
  registration. Lifecycle tools (`mc_launch` etc) are MC-specific
  and live in the MC repo's `UiMcpHost` project, not here. This bead
  ships the library pieces that `UiMcpHost` references from NuGet.

## New beads priority

- M1-21 and M1-22 are **M1 additions** — they land in the same phase
  as M1-01 through M1-20. Place them after M1-19 (which handles
  CoLocated stdout takeover) so M1-19 clearly scopes to CoLocated
  before M1-21 introduces the parallel Brokered path.
- M2-21 is an **M2 addition** — the broker scaffolding is consumed
  by MC's `UiMcpHost` at MC-1; snoopwpf M2 publishes the packages MC
  depends on.

## Tracker sync (`br`) commands

If updating the `.beads/` tracker (`br` binary currently has a GLIBC
mismatch under WSL; run these from a Windows-native shell or from
Linux where `br` works):

```bash
# Remove the two shim beads
br delete M2-13 --reason "Shim moved to MC repo as UiMcpActionCatalog per arch change 2026-04-16"
br delete M2-14 --reason "Shim moved to MC repo as UiMcpActionCatalog per arch change 2026-04-16"

# Update existing beads
br update M1-01 --add-note "2026-04-16: add Brokered to SessionMode enum; see ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md"
br update M1-04 --add-note "2026-04-16: clarify MF-11 forces redaction only in Injection; CoLocated + Brokered pass through"
br update M1-16 --add-note "2026-04-16: narrow SWPF0001 analyzer scope — CoLocated targets and Brokered brokers need it; Brokered targets do NOT"
br update M1-19 --add-note "2026-04-16: scope clarified — applies to StartCoLocated only; StartBrokered does not take over stdout"
br update M2-19 --add-note "2026-04-16: replaced by MC-side beads in /c/work/desktop/wpf-mcp/PRD-snoop-integration.md; this bead now covers only snoopwpf-side publication of pre-release packages"

# Create new beads
br create "M1-21: SnoopAgent.StartBrokered API" -p 1 --type task \
  --note "New brokered-mode entrypoint; see ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md"
br create "M1-22: Brokered-mode pipe framing" -p 1 --type task \
  --note "Reuse SnoopWPF.Agent.Remote framing; in-order guarantee"
br create "M2-21: SnoopWPF.Agent.Host broker scaffolding" -p 1 --type task \
  --note "Generic MCP stdio + pipe router + tool proxy registration for external brokers (including MC's UiMcpHost)"

# Record dependencies
br dep add M1-21 M1-01  # StartBrokered depends on SessionMode.Brokered
br dep add M2-21 M1-21  # Broker scaffolding depends on StartBrokered
br dep add M2-21 M1-22  # Broker scaffolding depends on pipe framing
```

Run after updating: `br sync --flush-only` to write JSONL for commit.

## Consumer-side doc

The MotionCatalyst-side consumer PRD at
`/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` has been updated
with the brokered architecture. Consumer beads will live in that repo
under `BEADS-MC-MVP.md` (to be written at MC-0). Key consumer-side
facts the snoopwpf implementer should know:

- The broker process on MC side is named **`UiMcpHost.exe`** (replaces
  `McpFlaUIHelper.exe`). Neither "Snoop" nor "FlaUI" appears in new
  desktop-side names per explicit user constraint.
- `UiMcpHost` references `SnoopWPF.Agent.Host`, `.Remote`, `.Contracts`
  from NuGet. It adds MC-specific lifecycle tools (`mc_launch`,
  `mc_exit`, `mc_restart`, `mc_attach_pid`) on top.
- MC target process references `SnoopWPF.Agent.Server` and calls
  `SnoopAgent.StartBrokered(app, pipeName, opts)` from its
  Dispatcher.

## Questions / edge cases (flag if encountered during bead work)

1. **Pipe-instance count**: current `maxAllowedInstances=1` assumes
   single broker per target. If a user runs two brokers against one
   MC, second pipe-connect will block. Acceptable for MVP; document.
2. **Pipe-name collision**: MC-side broker should use
   `{base}-{random}` pipe names to avoid collision when multiple MC
   instances run concurrently. Broker tells target the name via
   `--snoop-pipe=<name>` arg. Snoopwpf side just accepts the name as
   a parameter; name generation is broker responsibility.
3. **Broker → target authentication**: MVP ships without
   broker-target auth on the pipe. Pipe ACL defaults to current user
   only (from v3 hardening). If multi-user or elevated-user scenarios
   emerge post-MVP, revisit. Document as known limitation.

---

*End of change notice. Re-read PRD-v5-MVP.md §4 before continuing any
affected bead.*
