# SnoopWPF.Agent — Implementation Beads v5-MVP

> **ARCHITECTURE CHANGE 2026-04-16** — Brokered mode added as a third
> integration mode alongside CoLocated and Injection. Read
> `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md` in this directory
> BEFORE starting any affected bead. Then re-read `PRD-v5-MVP.md` §4.1,
> §4.2, §9.7 for the normative definition.
>
> Affected existing beads: **M1-01** (rename Start→StartCoLocated + Brokered
> enum), **M1-04**, **M1-06** (add testBigList fixture), **M1-10** (add
> TargetNotRunning), **M1-13** (brokered audit target-only), **M1-16**
> ([McpStdioEntrypoint] attribute name), **M1-19**, **M1-21** (hardened pipe),
> **M1-22** (concrete pre-flight grep), **M2-04b** (uses M1-06 fixture),
> **M2-13** (remove), **M2-14** (remove), **M2-15** (split into 15a+15b),
> **M2-16** (retarget from Shim.FlaUI), **M2-17** (brokered-suite replaces
> flaui-suite), **M2-18** (BrokerHost library), **M2-19** (SampleApp flags),
> **M2-21** (new BrokerHost library), **MP-02** (GitHub Security Advisories).
>
> New beads required: **M1-21** (`StartBrokered` API), **M1-22**
> (brokered-mode pipe framing), **M2-21** (`SnoopWPF.Agent.Host` broker
> scaffolding).
>
> The MC-side consumer PRD at
> `/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` describes the
> desktop-side broker process (`UiMcpHost.exe`, replaces
> `McpFlaUIHelper.exe`). The snoopwpf agent does not implement
> `UiMcpHost` — that is MC-side work. Snoopwpf ships the generic
> broker scaffolding that `UiMcpHost` consumes from NuGet.

> Generated from `PRD-v5-MVP.md` (scope-frozen, 18 agent-visible + 4 utility tools,
> 3 milestones, P50 18–22 weeks). Companion to `BEADS.md` v6 (the 30 beads that
> shipped the v3 substrate). This document takes over at M-pre and runs through M2
> close. Nothing in BEADS.md v6 is superseded — v6 beads are "closed," this file
> is "open."
>
> **How to read this file**
> - Every bead is one atomic work unit completable in a single agent session.
> - Beads are ordered by dependency. Execute top-to-bottom; parallelizable beads
>   are marked `[PARALLEL WITH: ...]`.
> - Every bead ends with acceptance criteria as **runnable commands**. If a
>   criterion cannot be checked by a command, the bead has been sized too large
>   and must be split.
> - Every bead ends with a commit message template. Use it verbatim (one bead =
>   one atomic commit per `~/.claude/CLAUDE.md` swarm rules).
> - Execute beads on `develop`. Do not create worktrees. Do not batch commits.
>
> **Cross-references**
> - `PRD-v5-MVP.md` §N is the normative spec. Each bead cites `PRD §N`.
> - `PRD-v5-ideal.md` is the North Star (72 tools, 11 milestones). Do NOT implement
>   from it; v5-MVP is the scope-frozen subset we ship.
> - `PRD-v5-reviews-wave2.md`, `PRD-v5-reviews-wave3.md` contain full reviewer
>   synthesis. Every Wave-2/Wave-3 finding cited in PRD-v5-MVP is reproduced in
>   the relevant bead's context block.
> - `BEADS.md` v6 is the predecessor. Bead IDs from v6 (BEAD-001..BEAD-028) do NOT
>   reappear here. MVP uses a different ID scheme (MP/M0/M1/M2) so the two files
>   never collide.

---

## Global Build Rules (apply to ALL new .csproj files)

1. **Central Package Management.** Every new `<PackageReference>` **must** have a
   matching `<PackageVersion>` entry in `Directory.packages.props`. Omit the
   `Version` attribute from the `<PackageReference>`. NU1104 at restore means you
   forgot this.
2. **LangVersion.** `Directory.build.props` now sets `<LangVersion>latest</LangVersion>`
   globally (set by BEAD-027). Do not override per-project.
3. **Nullability.** `Nullable=enable` is global. Every new .cs file opts in.
   Use `[MemberNotNullWhen]`, `[NotNullWhen]` where helpful; do not suppress with
   `#nullable disable`.
4. **TreatWarningsAsErrors.** Global. If you need a transient suppression, add
   it to the project's `<NoWarn>` with a `<!-- BEAD-XXX: reason -->` comment.
5. **Multi-targeting.** Agent production projects target
   `net462;net6.0-windows;net8.0-windows` except:
   - `SnoopWPF.Agent.Server`, `SnoopWPF.Agent.Tools` — `net8.0-windows` only
     (ModelContextProtocol SDK requires .NET 8+).
   - `SnoopWPF.Agent.Input.Deterministic` — `net8.0-windows` only in MVP. Multi-
     target is deferred to v2.0 when L3/L4 ship (UnsafeAccessor needs net8).
   - Test projects — `net8.0-windows` only.
6. **DataContract DTOs.** Every new DTO under `SnoopWPF.Agent.Contracts/Dtos/`
   must carry `[DataContract]` and every member `[DataMember(Name="camelCase")]`.
   DataContractJsonSerializer (net462) and System.Text.Json (net8) must round-trip
   the same JSON. Never use `Dictionary<string, string>` — use
   `List<NameValuePairDto>` (DataContract-serializable).
7. **Build from WSL.** All build commands run from `/c/work/snoopwpf/` using the
   Windows dotnet SDK: `/c/Program\ Files/dotnet/dotnet.exe` (aliased as
   `dotnet.exe` in WSL). The Linux `dotnet` cannot build WPF. Acceptance-criteria
   commands assume `dotnet.exe`.
8. **Commit hygiene.** Every bead commits with `git add <specific files>`; never
   `git add -A`. Planning artifacts (`BEADS-MVP.md`, `PRD-v5-MVP.md`) are only
   committed in the bead that touches them.

---

## Global Security Rules (apply to ALL M-pre + M0 + M1 + M2 beads)

These are additive to the seven rules in `BEADS.md` v6 (still in force). New for
v5-MVP:

- **S1. Session policy is immutable per session.** Construct `SessionPolicy` once
  at session start; never mutate it. Tool handlers take a `SessionPolicy` reference,
  never a mutable copy.
- **S2. Strategy-layer gating is authoritative.** `EnableAutomation`,
  `EnableMutation`, `MaxTier` are enforced in `InputStrategySelector`, not in tool
  handlers. Adding a new tool that bypasses the selector is a security regression;
  the Roslyn analyzer flags direct `DependencyObject.SetValue` calls outside the
  strategy layer.
- **S3. MF-10 structural redaction.** `Redact()` inspects the **runtime type** of
  a property value against a structural-sensitivity map before invoking
  `ToString()`. Minimum map: `SecureString`, `NetworkCredential`, any subtype of
  `System.Data.Common.DbConnectionStringBuilder`, any type marked `[Sensitive]`.
  Property-name keyword matching alone is insufficient.
- **S4. MF-11 forced injection redaction.** `SessionPolicy.Create(Injection, opts)`
  overrides `opts.EnableRedaction` to `true` before returning. Co-located mode
  respects caller choice. The override lives in the factory, never in tool code.
- **S5. HMAC audit log ordering.** Writes flow through a single
  `Channel<AuditEntry>` worker thread. The worker is the sole HMAC state holder.
  Chain format: `HMAC-SHA256(entryJson || prevHmac || sessionKey || counterNonce)`.
  Session key from `RandomNumberGenerator.GetBytes(32)`, never from process
  identity. The Channel writer is the only place that reads/writes the chain
  tail.
- **S6. Stdout takeover.** `SnoopAgent.StartCoLocated` first statement is
  `Console.SetOut(TextWriter.Null)`. Analyzer `SWPF0001` flags `Console.Write*`
  in assemblies marked `[McpStdioEntrypoint]` (CoLocated targets, Brokered
  brokers, and Injection hosts; NOT Brokered targets).
- **S7. Injection mode is inspection-only in MVP.** `InputStrategySelector` refuses
  to construct L0/L1 strategies when `SessionPolicy.Mode == Injection`. Any future
  L3/L4 enablement lands in v2.0 with its own security review.
- **S8. Secrets never in logs.** Exception payloads, `reason` fields in audit
  entries, tool-error `message` fields must be sanitized (newlines stripped,
  256-char cap, property values stripped).

---

## Frozen Design Decisions (signatures — implementers must not alter)

These decisions are frozen at PRD ratification. Any M1/M2 bead that proposes
altering them is a PRD amendment, not a bead.

### FD-1 `WpfLocator` (PRD §6)

```csharp
// SnoopWPF.Agent.Contracts/WpfLocator.cs
[DataContract]
public sealed record WpfLocator
{
    [DataMember(Name = "form")]   public WpfLocatorForm Form { get; init; }
    [DataMember(Name = "value")]  public string Value { get; init; } = "";
    [DataMember(Name = "raw")]    public string Raw { get; init; } = ""; // original $locator string
}

public enum WpfLocatorForm
{
    AutomationId = 0,   // "automationId=StartButton"         — strongest
    ViewModel = 1,      // "viewModel=SessionVm, property=..., value=..."
    TypeName = 2,       // "type=Button, name=Start"
    Path = 3,           // "path=Window\\Grid\\StackPanel\\Button"  — weakest
}
```

Parsing: see bead **M1-10**. Stability ordering documented in PRD §6.

### FD-2 `SessionPolicy` (PRD §4.3)

```csharp
// SnoopWPF.Agent.Contracts/SessionPolicy.cs
[DataContract]
public sealed record SessionPolicy
{
    [DataMember(Name = "mode")]                    public SessionMode Mode { get; init; }
    [DataMember(Name = "maxTier")]                 public InputTier MaxTier { get; init; } // L0 | L1 in MVP
    [DataMember(Name = "enableAutomation")]        public bool EnableAutomation { get; init; }
    [DataMember(Name = "enableMutation")]          public bool EnableMutation { get; init; }
    [DataMember(Name = "enableRedaction")]         public bool EnableRedaction { get; init; }
    [DataMember(Name = "redactionPolicy")]         public RedactionPolicy RedactionPolicy { get; init; } = RedactionPolicy.Default;
    [DataMember(Name = "allowSensitiveRetention")] public bool AllowSensitiveRetention { get; init; }

    public static SessionPolicy Create(SessionMode mode, SnoopAgentOptions opts)
    {
        var enableRedaction = mode == SessionMode.Injection ? true : opts.EnableRedaction; // MF-11
        var maxTier = mode == SessionMode.Injection ? InputTier.L0ReadOnly : opts.MaxTier; // S7
        // CoLocated and Brokered: pass opts through unchanged (owned apps, caller-chosen redaction).
        // Injection: force EnableRedaction=true, cap MaxTier=L0ReadOnly.
        // Brokered: identical to CoLocated for policy purposes (full L1, caller owns redaction choice).
        // ...
    }
}

public enum SessionMode { CoLocated = 0, Brokered = 1, Injection = 2 }
public enum InputTier   { L0ReadOnly = 0, L0 = 1, L1 = 2 /* L2/L3/L4 deferred */ }
```

### FD-3 `StateDelta` response shape (PRD §7)

```csharp
// SnoopWPF.Agent.Contracts/Dtos/StateDeltaDto.cs
[DataContract]
public sealed record StateDeltaDto
{
    [DataMember(Name = "success")]                   public bool Success { get; init; }
    [DataMember(Name = "elementVisible")]            public bool ElementVisible { get; init; }
    [DataMember(Name = "stateChanged")]              public bool StateChanged { get; init; } // §7.3 computed at serialization time
    [DataMember(Name = "currentFocus")]              public WpfLocator? CurrentFocus { get; init; }
    [DataMember(Name = "treeVersionDelta")]          public int TreeVersionDelta { get; init; }
    [DataMember(Name = "actionabilityChecksFailed")] public List<string>? ActionabilityChecksFailed { get; init; } // failure only
    [DataMember(Name = "failureReason")]             public FailureReason? FailureReason { get; init; }
    [DataMember(Name = "suggestion")]                public SuggestionDto? Suggestion { get; init; }
    // Debug-only fields (omit unless options.Debug == true):
    [DataMember(Name = "treeVersionBefore", EmitDefaultValue = false)] public long? TreeVersionBefore { get; init; }
    [DataMember(Name = "treeVersionAfter",  EmitDefaultValue = false)] public long? TreeVersionAfter { get; init; }
    [DataMember(Name = "elapsedMs",         EmitDefaultValue = false)] public long? ElapsedMs { get; init; }
    [DataMember(Name = "chosenTier",        EmitDefaultValue = false)] public InputTier? ChosenTier { get; init; }
}

public enum FailureReason
{
    ElementNotFound = 0,
    ElementNotVisible = 1,
    ElementNotEnabled = 2,
    CannotExecuteCommand = 3,
    AutomationDisabled = 4,
    MutationDisabled = 5,
    TierMismatch = 6,
    StateUnchanged = 7,
    LocatorAmbiguous = 8,
    DispatcherBusy = 9,
    ElementOutsideViewport = 10,
    PatternNotSupported = 11,
    TargetNotRunning = 12,
}

[DataContract]
public sealed record SuggestionDto
{
    [DataMember(Name = "tool")] public string Tool { get; init; } = "";
    [DataMember(Name = "args")] public List<NameValuePairDto> Args { get; init; } = new();
}
```

### FD-4 `IIdlingResource` contract (PRD §8.2)

```csharp
// SnoopWPF.Agent.Contracts/IIdlingResource.cs
public interface IIdlingResource
{
    string Name { get; }
    bool IsIdle { get; }
    DispatcherPriority CheckPriority { get; } // must be ≥ ContextIdle, per W3-H1
    event EventHandler<IdleChangedEventArgs>? IdleChanged;
}

public sealed class IdleChangedEventArgs : EventArgs
{
    public required bool IsIdle { get; init; }
    public required DateTimeOffset At { get; init; }
}
```

Built-ins (bead M1-12): Dispatcher, CompositionRendering, Storyboard,
DispatcherTimer, Custom.

### FD-5 `IDeterministicInputStrategy` (PRD §4.4)

```csharp
// SnoopWPF.Agent.Contracts/IDeterministicInputStrategy.cs
public interface IDeterministicInputStrategy
{
    InputTier Tier { get; } // L0 or L1 in MVP
    bool CanHandle(InputIntent intent, DependencyObject target);
    DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct);
}

[DataContract]
public sealed record DeterministicInputResult
{
    [DataMember(Name = "success")]        public bool Success { get; init; }
    [DataMember(Name = "failureReason")]  public FailureReason? FailureReason { get; init; }
    [DataMember(Name = "previousValue")]  public string? PreviousValue { get; init; } // for stateChanged computation
    [DataMember(Name = "chosenTier")]     public InputTier ChosenTier { get; init; }
}
```

### FD-6 `[Sensitive]` attribute (PRD §9.2)

```csharp
// SnoopWPF.Agent.Contracts/SensitiveAttribute.cs
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class
              | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute { }
```

### FD-7 `AuditEntry` (PRD §9.4)

```csharp
// SnoopWPF.Agent.Contracts/Audit/AuditEntry.cs
[DataContract]
internal sealed record AuditEntry
{
    [DataMember(Name = "seq")]           public long Seq { get; init; }
    [DataMember(Name = "at")]            public DateTimeOffset At { get; init; }
    [DataMember(Name = "toolName")]      public string ToolName { get; init; } = "";
    [DataMember(Name = "sessionId")]     public string SessionId { get; init; } = "";
    [DataMember(Name = "outcome")]       public string Outcome { get; init; } = ""; // "ok"|"fail"
    [DataMember(Name = "reason")]        public string? Reason { get; init; }       // sanitized, 256-char cap
    [DataMember(Name = "counterNonce")]  public long CounterNonce { get; init; }
    [DataMember(Name = "hmac")]          public string Hmac { get; init; } = "";    // hex
}
```

---

## Quality Gates (run after every bead)

Copy the block under each bead's acceptance criteria. If you change these
commands, add a note to the PRD↔Beads divergence log at the end of this file.

```bash
# Build gate (mandatory every bead)
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true

# Unit test gate (mandatory for beads that touch .cs in any .Tests project)
dotnet.exe test /c/work/snoopwpf/Snoop.Core.Tests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests

# Integration test gate (mandatory for beads that change tool handlers, SnoopInspector,
# or anything the IntegrationTests drive end-to-end)
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests

# Injection test gate (mandatory for beads that change PipeAgentServer,
# PipeConnection, or Injection project)
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.InjectionTests
```

---

## ID Scheme & Dependency Graph

IDs are `{phase}-{NN}` with two-digit numbers to preserve sort order.

| Phase  | Prefix | Range      | Count | Scope |
|--------|--------|------------|-------|-------|
| M-pre  | `MP-`  | 01–03      | 3     | Close BEAD-028, fix drift, unblock M0 |
| M0     | `M0-`  | 01–07      | 7     | Spikes (S-1/2/3/3b/5) + pre-M1 audits (PR-1/PR-2) |
| M1     | `M1-`  | 01–22      | 22    | Session policy, locator, state delta, idle contract, redaction, audit, analyzers, StartBrokered (M1-21), pipe framing (M1-22) |
| M2     | `M2-`  | 01–21      | 19    | L0/L1 act tools, extract, sync, VeriGUI (M2-15a+15b), CI, NuGet, BrokerHost library (M2-21); M2-13 and M2-14 REMOVED per 2026-04-16 arch change |
| Total  |        |            | 51    | |

**Dependency graph (ASCII DAG, top blocks bottom)**

```
MP-01 MP-02 MP-03           ─ all three parallel
  └───┬─────┘
      ▼
M0-01  M0-02  M0-03           ─ M0-01..03 parallel
                └─► M0-04     ─ S-3b blocked by S-3 (M0-03)
      M0-05                   ─ S-5 parallel with M0-01..04
                M0-06 M0-07    ─ parallel with each other, block M1 start

   ┌────────── M1-01 (SessionPolicy) ──────────┐
   │                                          │
   ▼                                          ▼
 M1-02 ([Sensitive])        M1-03 (structural redaction map)
   │                                          │
   └─► M1-04 (forced injection redaction) ◄───┘
         │
         ▼
 M1-05 (WpfLocator parse) ──► M1-06 (ISnoopInspector locator overloads)
         │                            │
         ▼                            │
 M1-07 (find_elements w/ hasCommandBinding) │
         │                            │
         ▼                            │
 M1-08 (get_session_info windows inline) ◄──┘
         │
         ▼
 M1-09 (StateDelta schema) ──► M1-10 (FailureReason enum + Suggestion)
         │                            │
         ▼                            │
 M1-11 (stateChanged at serialization)│
         │                            │
         ▼                            │
 M1-12 (IIdlingResource contract + 4 built-ins)
         │
         ▼
 M1-13 (HMAC audit writer on Channel<AuditEntry>)
         │
         ▼
 M1-14 (Input.Deterministic project scaffold)
         │
         ▼
 M1-15 (IDeterministicInputStrategy + InputStrategySelector)
         │
         ▼
 M1-16 (SWPF0001 analyzer) ── parallel ── M1-17 (SWPF0010)  ── parallel ── M1-18 (SWPF0011)
         │
         ▼
 M1-19 (Console.Out takeover + bug fixes) ── parallel ── M1-20 (UnsafeAccessor self-test + HwndSource precondition)
         │
         ▼
 M1-21 (StartBrokered API — hardened pipe + reconnect loop)
         │
         ▼
 M1-22 (pipe framing — FramedJsonTransport, in-order guarantee)

 ─── M2 begins after M1-22 ───

 M2-01 wpf_execute_command (L0)           M2-05 wpf_click (L1)
   │                                        │
   ▼                                        ▼
 M2-02 wpf_set_text_value (L0)            M2-06 wpf_toggle (L1)
   │                                        │
   ▼                                        ▼
 M2-03 wpf_set_check_state (L0)           M2-07 wpf_expand_collapse (L1)
   │
   ▼
 M2-04a wpf_select_item (L0 basic)
   │
   ▼
 M2-04b wpf_select_item (virtualized scroll)

 M2-08 wpf_resolve_binding (Extract)
 M2-09 wpf_wait_for_property ── M2-10 wpf_poll_changes ── M2-11 wpf_pump_until_idle
 M2-12 wpf_fetch_blob
 [M2-13 REMOVED — shim moved to MC repo per 2026-04-16 arch change]
 [M2-14 REMOVED — same rationale as M2-13]
 M2-15a VeriGUI harness project + runner + CI
   │
   ▼
 M2-15b VeriGUI 100 scenarios
 M2-16 Coverage-gap closure per PR-1 outcome
 M2-17 CI dual-stack finalize (builds on M0-07 PR-2)
 M2-18 NuGet packaging + signing + feed decision
 M2-21 SnoopWPF.Agent.BrokerHost library (new ClassLibrary; depends on M1-21, M1-22)
   │
   ▼
 M2-19 Brokered-mode consumer deliverables (snoopwpf-side only)
```

Parallelization allowed where explicit `[PARALLEL WITH: ...]` is listed on the
bead. Absence of the marker means serialize.

---

# Phase M-pre — Cleanup (unblocks M0)

> PRD §10 M-pre. Three small beads, fully parallel, land before any M0 spike. Goal:
> zero known drift between docs and code, zero unpushed commits when M0 starts.

## MP-01: Fix volatility inconsistency on `disposed` fields

**PRD ref:** PRD-v5-MVP §10 M-pre bullet 3
**Blocks:** none hard; M0-03 benefits from it (S-3 benchmark touches NodeRegistry)
**[PARALLEL WITH: MP-02, MP-03]**
**Estimated:** 2 files, ~4 LOC, 10 min.

**Context.** `SnoopInspector` and `PipeAgentServer` already declare
`private volatile bool disposed`. `NodeRegistry` and `CursorManager` declare plain
`private bool disposed`. Under the dispose-then-access race, plain `bool` is
not guaranteed to publish to other threads on weak-memory architectures.

**Files to edit**

- `SnoopWPF.Agent.Engine/Infrastructure/NodeRegistry.cs`
- `SnoopWPF.Agent.Engine/Infrastructure/CursorManager.cs`

**Pre-flight verification**

```bash
# Confirm current (missing-volatile) state exists
grep -n "private bool disposed" /c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/NodeRegistry.cs
grep -n "private bool disposed" /c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/CursorManager.cs
# Confirm volatile precedent
grep -n "private volatile bool disposed" /c/work/snoopwpf/SnoopWPF.Agent.Engine/SnoopInspector.cs
```

**Steps**

1. `NodeRegistry.cs`: change `private bool disposed;` to `private volatile bool disposed;`.
2. `CursorManager.cs`: same edit.
3. Build.
4. Commit.

**Acceptance criteria**

```bash
grep -nE "private volatile bool disposed" /c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/NodeRegistry.cs
grep -nE "private volatile bool disposed" /c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/CursorManager.cs
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests --filter "FullyQualifiedName~NodeRegistry|FullyQualifiedName~CursorManager"
```

**Commit**

```
fix(MP-01): volatile disposed on NodeRegistry and CursorManager

Matches volatile precedent on SnoopInspector and PipeAgentServer. Closes
weak-memory dispose-vs-access publication gap flagged in PRD-v5-MVP §10.
```

---

## MP-02: Close BEAD-028 — SECURITY.md, CHANGELOG.md 1.0.0 entry, SnoopLog ACL decision

**PRD ref:** PRD-v5-MVP §10 M-pre bullet 1
**Blocks:** none hard (documentation/CI); valuable to close before M0 for CI stability.
**[PARALLEL WITH: MP-01, MP-03]**
**Estimated:** 3 files new, 1 file edited, ~150 LOC, 1 hr.

**Context.** BEAD-028 from `BEADS.md` v6 had 5 acceptance criteria; 2 shipped
(mcp-agent docs and integration test doc), 3 unmet. This bead closes them so
release tooling (NuGet, SBOM) can reference a single canonical `SECURITY.md`.

**Files to create/edit**

- **Create** `/c/work/snoopwpf/SECURITY.md` — root-level disclosure policy.
- **Edit** `/c/work/snoopwpf/Changelog.md` — add a `1.0.0` entry summarising
  v3 + review-fix wave.
- **Decide** SnoopLog ACL: either (a) implement ACL + per-session rotation in
  `Snoop.InjectorLauncher/Injector.cs` **or** (b) delete the false claim from
  `docs/security.md:180`. **Do (b) for MVP.** Implementing (a) is a separate
  bead deferred to v1.1 because the logger is used under exception paths and
  ACL failure must not crash the injection host.

**Pre-flight verification**

```bash
ls /c/work/snoopwpf/SECURITY.md 2>&1 | grep -q "No such file"   # expect no-such-file
grep -n "1.0.0" /c/work/snoopwpf/Changelog.md                   # expect zero hits
grep -n "SnoopLog" /c/work/snoopwpf/docs/security.md            # expect hit on line ~180
grep -n "AppendText" /c/work/snoopwpf/Snoop.InjectorLauncher/Injector.cs
```

**Steps**

1. **Create `SECURITY.md`** covering:
   - Scope (this fork, InitialForce/snoopwpf).
   - Supported versions (develop + latest tag).
   - Private disclosure channel: **GitHub Security Advisories**. Contact
     field = link to the repo's `/security/advisories/new` URL
     (`https://github.com/InitialForce/snoopwpf/security/advisories/new`).
     Do not list an email address — GitHub Security Advisories is the chosen
     disclosure channel.
   - Response timeline.
   - Cryptographic primitives actually shipped (HMAC-SHA256 audit chain, pipe
     ACL `CurrentUserOnly`, 256-bit session token via RandomNumberGenerator).
   - Known non-goals: no TLS (localhost-only pipes), no remote MCP, no auth
     model beyond token handshake.
2. **Changelog** — add under `## 1.0.0 (unreleased)`:
   - MCP agent surface (15 v3 tools, two modes).
   - Review-fix wave: stdout redirect, CursorManager CAS, NodeRegistry
     atomic clear, RedactionFilter dead-code removal, error-order fix,
     pipe handshake timeout + constant-time compare.
   - Known limitations carried to v1.1 (see PRD §16).
3. **Remove** the SnoopLog ACL claim from `docs/security.md` (find line ~180
   that begins with a sentence describing `SnoopLog.txt` as ACL'd + rotated;
   replace with: "`SnoopLog.txt` is a plain append log written via
   `FileInfo.AppendText`. No ACL or rotation is applied. Treat it as
   containing potentially sensitive payloads when the injection launcher
   surfaces an exception. Deletion on next session start is the user's
   responsibility in MVP; ACL + rotation is tracked for v1.1.")

**Acceptance criteria**

```bash
test -f /c/work/snoopwpf/SECURITY.md
grep -q "security@initialforce" /c/work/snoopwpf/SECURITY.md || grep -q "security advisor" /c/work/snoopwpf/SECURITY.md
grep -q "^## \[1.0.0\]" /c/work/snoopwpf/Changelog.md || grep -q "^## 1.0.0" /c/work/snoopwpf/Changelog.md
# Prove SnoopLog drift was removed
! grep -q "SnoopLog.txt is ACL" /c/work/snoopwpf/docs/security.md
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
```

**Commit**

```
docs(MP-02): close BEAD-028 — SECURITY.md, CHANGELOG 1.0.0, SnoopLog drift fix

- Add root SECURITY.md with disclosure policy and crypto primitives shipped.
- Add 1.0.0 entry to Changelog.md covering v3 + review-fix wave.
- Remove false ACL/rotation claim from docs/security.md:180; document the
  plain-append behaviour of Snoop.InjectorLauncher/Injector.cs:29. ACL +
  rotation deferred to v1.1.
```

---

## MP-03: Full-repo doc-vs-code integrity sweep

**PRD ref:** PRD-v5-MVP §10 M-pre bullet 4
**Blocks:** none hard; mandatory for PRD-as-reference trust during M1 implementation.
**[PARALLEL WITH: MP-01, MP-02]**
**Estimated:** 1 hr + edits; typical outcome 0–4 small drift fixes.

**Context.** MP-02 found one false claim in `docs/security.md`. Before M1
starts (where PRD-v5-MVP is cited heavily), sweep all security-relevant docs
for similar drift. This is a one-shot audit; if it turns up >4 drift items the
bead fails-forward and opens follow-up beads instead of closing them inline.

**Files to audit (read-first, edit only on drift)**

- `/c/work/snoopwpf/docs/security.md`
- `/c/work/snoopwpf/docs/mcp-agent.md`
- `/c/work/snoopwpf/docs/nuget-mode.md`
- `/c/work/snoopwpf/docs/injection-mode.md`
- `/c/work/snoopwpf/docs/mcp-tools-reference.md`
- `/c/work/snoopwpf/README.md` (security section)
- `/c/work/snoopwpf/Changelog.md` (security notes)
- `/c/work/snoopwpf/SECURITY.md` (created in MP-02)

**Audit protocol**

For each claim of the form "X uses Y algorithm" or "X has Y protection" or
"X is enforced in module Z", find the code and confirm. Record findings in
a scratch file at `/tmp/mp03-audit.md`; commit only the actual drift fixes.

**Specific claims to check (from PRD-v5-MVP review)**

1. `docs/security.md` — "256-bit session token": confirm against
   `SnoopAgent.cs` `RandomNumberGenerator.GetBytes(32)`.
2. `docs/security.md` — "CurrentUserOnly pipe ACL": confirm against
   `McpServerSetup.cs` RunWithPipeAsync.
3. `docs/security.md` — "FixedTimeEquals constant-time compare": confirm
   against `PerformPipeHandshakeAsync`.
4. `docs/mcp-agent.md` — "15 MCP tools": count tools in
   `SnoopWPF.Agent.Tools/` registrations.
5. `docs/security.md` — "21 keyword redaction list": count entries in
   `RedactionFilter.cs`.
6. Any "X project targets net462;net6.0-windows;net8.0-windows" claim: check
   against `.csproj`.

**Steps**

1. Read each audit file. For each factual claim, find the backing code
   location and confirm.
2. Record drift in `/tmp/mp03-audit.md`.
3. For drift items ≤4: edit each file to match code (prefer editing docs over
   code — docs are easier to change, and this bead's contract is docs-vs-code
   truth, not changing behaviour).
4. For drift items >4: STOP. Commit `/tmp/mp03-audit.md` as
   `/c/work/snoopwpf/DOC-DRIFT-REPORT.md` and open individual beads for each
   item; do not fix inline.

**Acceptance criteria**

```bash
# The sweep output exists (either committed as DOC-DRIFT-REPORT.md, or referenced
# in commit message if drift count ≤4 and was fixed inline).
test -f /c/work/snoopwpf/DOC-DRIFT-REPORT.md || git log -1 --format=%B | grep -q "mp03\|MP-03"
# No build regression
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
```

**Commit**

```
docs(MP-03): full-repo doc-vs-code integrity sweep

<One line per drift item fixed, e.g.>
- docs/security.md: correct keyword list count from 21 to 19.
- README.md: update project-layout row for SnoopWPF.Agent.Server target.

Closes PRD-v5-MVP §10 M-pre bullet 4.
```

---

# Phase M0 — Spikes + Pre-M1 De-Risk

> PRD §10 M0. Five spikes + two audits before M1 code lands. All spike results
> written to `SPIKE-RESULTS.md`. M1 beads do not open until every spike is green
> or has a documented fallback.

## M0-01: Spike S-1 — Co-located stdio round-trip re-verification

**PRD ref:** PRD-v5-MVP §10 M0, spike S-1
**Blocks:** none hard (sanity only); proves integration harness still works before M1.
**[PARALLEL WITH: M0-02, M0-03, M0-05]**
**Estimated:** no new code; run existing tests and document; 1 day.

**Context.** The existing `SnoopWPF.Agent.IntegrationTests.IntegrationTestFixture`
+ `TestWpfApp` already proves stdio round-trip. PRD calls this spike
"academic" but requires a re-run sanity check before M1.

**Steps**

1. Run the full integration suite on Windows.
2. Extract per-call latency numbers from xUnit output (or add
   `DiagnosticMessageSink` output if missing).
3. Write `SPIKE-RESULTS.md` at repo root with section `## S-1` containing:
   - Test machine spec (CPU, RAM, .NET SDK version).
   - Total integration-test run time.
   - Slowest / fastest tool call (ms).
   - Verdict: GREEN / YELLOW / RED.
4. Commit only `SPIKE-RESULTS.md`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests
test -f /c/work/snoopwpf/SPIKE-RESULTS.md
grep -q "^## S-1" /c/work/snoopwpf/SPIKE-RESULTS.md
grep -q "GREEN\|YELLOW\|RED" /c/work/snoopwpf/SPIKE-RESULTS.md
```

**Commit**

```
docs(M0-01): S-1 stdio round-trip spike verification

Integration suite green on net8.0-windows; p95 tool-call latency <X ms.
SPIKE-RESULTS.md created for subsequent M0 spike entries.
```

---

## M0-02: Spike S-2 — UnsafeAccessor compilation on net6/8/9

**PRD ref:** PRD-v5-MVP §10 M0, spike S-2
**Blocks:** none in MVP (L3/L4 deferred); gates v2.0.
**[PARALLEL WITH: M0-01, M0-03, M0-05]**
**Estimated:** 2 .cs files (throwaway), 1 day.

**Context.** .NET 8 `[UnsafeAccessor]` will replace reflection for future L3
input strategies. MVP only needs to prove the attribute compiles on net6/8/9
and resolves private members correctly — detailed L3 validation waits.

**Files to create (throwaway — do not ship)**

- `/c/work/snoopwpf/spike/UnsafeAccessorSpike/UnsafeAccessorSpike.csproj`
- `/c/work/snoopwpf/spike/UnsafeAccessorSpike/Program.cs`

**Steps**

1. Minimal console app multi-targeting `net6.0-windows;net8.0-windows;net9.0-windows`.
2. Use `[UnsafeAccessor(UnsafeAccessorKind.Method, Name="GetHwnd")]` on
   `HwndSource` (a target likely to exist in L3 strategies).
3. Call it against a live `HwndSource` instance (create one in-process).
4. Report success/failure per TFM to `SPIKE-RESULTS.md` under `## S-2`.

**Acceptance criteria**

```bash
dotnet.exe build /c/work/snoopwpf/spike/UnsafeAccessorSpike/UnsafeAccessorSpike.csproj
grep -q "^## S-2" /c/work/snoopwpf/SPIKE-RESULTS.md
# Spike project is under spike/, not in Snoop.sln (do not add to solution):
! grep -q "UnsafeAccessorSpike" /c/work/snoopwpf/Snoop.sln
```

**Commit**

```
docs(M0-02): S-2 UnsafeAccessor compile spike

Verified [UnsafeAccessor] compiles and resolves HwndSource.GetHwnd on net6/8/9.
Detailed L3 validation deferred with L3. Spike project under spike/, not in
Snoop.sln.
```

---

## M0-03: Spike S-3 — Tree-change detection benchmark (critical path)

**PRD ref:** PRD-v5-MVP §10 M0, spike S-3 — **only real binary-outcome spike**
**Blocks:** M0-04 (S-3b circular-dep test), M1-12 (IIdlingResource built-ins)
**[PARALLEL WITH: M0-01, M0-02, M0-05]**
**Estimated:** 2 .cs files, 3–5 days.

**Context.** PRD target: < 1 µs per `NodeRegistry.Bump()` on a 200-node tree
under 1e6 mutation cycles. If `Panel.Children.CollectionChanged` synchronous
firing costs 3–5 µs, the four-source feed in §8.3 needs a coalescing ring
buffer — new subsystem. Know the answer BEFORE writing any M1 code.

**Files to create**

- `/c/work/snoopwpf/spike/TreeChangeBench/TreeChangeBench.csproj` (net8.0-windows,
  BenchmarkDotNet reference via Central Package Management — add
  `<PackageVersion Include="BenchmarkDotNet" Version="X.Y.Z" />` to
  `Directory.packages.props`).
- `/c/work/snoopwpf/spike/TreeChangeBench/Program.cs`

**Steps**

1. Build a 200-node WPF tree (nested `Grid` + `StackPanel` + `Button`).
2. Register all 200 nodes in a `NodeRegistry` (reuse production impl).
3. Benchmark four scenarios:
   - Pure `NodeRegistry.Bump()` hot-loop (baseline).
   - `Panel.Children.Add/Remove` with `CollectionChanged` hooked to `Bump()`.
   - `FrameworkElement.Loaded/Unloaded` on add/remove.
   - `CompositionTarget.Rendering` frame-tick walk, 200-nodes budget-capped
     to 200 µs.
4. Record per-scenario mean, p50, p95, p99 in `SPIKE-RESULTS.md` under `## S-3`.
5. **Decide**: if any scenario exceeds 1 µs/op, add a follow-up bead for
   coalescing ring buffer BEFORE closing this spike. Do NOT open M1-12 until
   this decision is written.

**Acceptance criteria**

```bash
dotnet.exe run --project /c/work/snoopwpf/spike/TreeChangeBench/TreeChangeBench.csproj -c Release
grep -q "^## S-3" /c/work/snoopwpf/SPIKE-RESULTS.md
grep -qE "Bump.*ns|Bump.*μs" /c/work/snoopwpf/SPIKE-RESULTS.md
grep -q "VERDICT" /c/work/snoopwpf/SPIKE-RESULTS.md
```

**Commit**

```
perf(M0-03): S-3 tree-change detection benchmark

Measured: <Bump>ns, <CollectionChanged>ns, <Loaded/Unloaded>ns, <Rendering>ns
on a 200-node tree. <VERDICT> ring-buffer needed / not needed. Results drive
M1-12 IIdlingResource implementation.
```

---

## M0-04: Spike S-3b — Circular-dependency test for poll_changes

**PRD ref:** PRD-v5-MVP §10 M0, spike S-3b (Wave-3 W3-H2)
**Depends on:** M0-03 (uses its NodeRegistry benchmark infra).
**Estimated:** 1 .cs test, 1 day.

**Context.** Tests that prove `wpf_poll_changes` works must NOT rely on
`wpf_wait_for_property` (which also polls), otherwise a bug in polling could
pass the test by making both calls hang together. Break the cycle: this test
uses `Thread.Sleep` + `wpf_poll_changes` only.

**Files to create/edit**

- `/c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests/PollChangesWithoutWaitForPropertyTest.cs`

**Steps**

1. Start a `TestWpfApp` with a `Button` (registered nodeId via
   `find_elements`).
2. Capture `treeVersion` via `wpf_get_session_info` (fallback: synthesize a
   minimal sentinel if the field isn't added yet — log a TODO; M1-08 adds it
   properly).
3. On dispatcher thread: remove the `Button` and mutate a property on its
   sibling.
4. `Thread.Sleep(50)`.
5. Call `wpf_poll_changes(sinceVersion: before)`.
6. Assert: returned `treeVersionDelta >= 1` and the removed nodeId appears
   in the changeset.
7. Key anti-pattern to avoid: no `wpf_wait_for_property` in this test.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~PollChangesWithoutWaitForProperty"
grep -q "^## S-3b" /c/work/snoopwpf/SPIKE-RESULTS.md
# Circular-dep anti-pattern check:
! grep -q "wait_for_property\|WaitForProperty" \
    /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests/PollChangesWithoutWaitForPropertyTest.cs
```

**Commit**

```
test(M0-04): S-3b circular-dep test — poll_changes without wait_for_property

Closes the Wave-3 W3-H2 hazard where poll_changes and wait_for_property could
both hide bugs by failing together. Uses Thread.Sleep + poll_changes only.
```

---

## M0-05: Spike S-5 — ValueChangedEventManager weak-event leak test

**PRD ref:** PRD-v5-MVP §10 M0, spike S-5
**[PARALLEL WITH: M0-01, M0-02, M0-03]**
**Estimated:** 1 .cs test, 2 days.

**Context.** PRD §8.3 uses `ValueChangedEventManager` for watched DPs. This
must be leak-free over millions of bumps, else long-running agent sessions
balloon GC pressure.

**Files to create**

- `/c/work/snoopwpf/SnoopWPF.Agent.Tests/WeakEventManagerLeakTest.cs`

**Steps**

1. Create a DP-bearing object.
2. Subscribe via `ValueChangedEventManager` (matches production usage — if
   production path doesn't exist yet, write a tiny harness mimicking the
   planned M1-12 usage and commit the harness under `tests/`).
3. Bump the DP 1,000,000 times.
4. Drop strong references, `GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced)`,
   `GC.WaitForPendingFinalizers()`, `GC.Collect()` again.
5. Assert: managed heap size (via `GC.GetTotalMemory(forceFullCollection: true)`)
   returns within 2 MB of baseline.
6. Write verdict to `SPIKE-RESULTS.md` under `## S-5`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~WeakEventManagerLeak"
grep -q "^## S-5" /c/work/snoopwpf/SPIKE-RESULTS.md
grep -qi "leak|bytes|mb" /c/work/snoopwpf/SPIKE-RESULTS.md
```

**Commit**

```
test(M0-05): S-5 weak-event leak spike

ValueChangedEventManager leak-free over 1e6 bumps. Verdict written to
SPIKE-RESULTS.md. Clears the long-running-session memory-pressure hazard
flagged in PRD §8.3.
```

---

## M0-06: Pre-M1 audit PR-1 — Coverage-gap audit vs MC SpecFlow scenarios

**PRD ref:** PRD-v5-MVP §10 M0 pre-M1 task PR-1
**Blocks:** M2-16 (coverage-gap closure) — without this audit M2 gate definition
is unverified. [M2-13 REMOVED per 2026-04-16 arch change.]
**[PARALLEL WITH: M0-07]**
**Estimated:** no code; research + 1 markdown file; 2 days.

**Context.** MC has 18 active SpecFlow scenarios. The MVP shim aims for 70%
coverage. This bead maps each of the 18 against the §12.3 30% gap list and
records a decision: (a) shim covers, (b) FlaUI stays, (c) descoped.

**Files to create**

- `/c/work/snoopwpf/COVERAGE-GAP-AUDIT.md` (committed to repo)

**Steps**

1. Obtain the MC scenario list — the consumer PRD lives at
   `/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` per PRD-v5-MVP intro.
   If not accessible from this session, list the 18 scenarios by title from
   an MC repo fetch; record exactly which source was used.
2. For each scenario, record: title, primary UI actions, current FlaUI calls,
   MVP tool equivalent, decision (shim/FlaUI/descope), rationale.
3. Gate: if 5+ scenarios need FlaUI fallback, add a note at the top of the
   document: `REWRITE-M2-GATE-REQUIRED`, and hand back to planner.
4. Commit the document.

**Acceptance criteria**

```bash
test -f /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
# 18 scenarios must appear as headings
grep -cE "^###? SCENARIO" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md | awk '$1 >= 18'
# Decision table present
grep -q "shim\|FlaUI\|descoped" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
```

**Commit**

```
docs(M0-06): PR-1 coverage-gap audit — MC 18 scenarios

<N> scenarios covered by MVP shim, <M> kept on FlaUI, <K> descoped. Gate
definition for M2 <stands / needs rewrite>. Drives M2-16.
```

---

## M0-07: Pre-M1 audit PR-2 — Headless CI wiring end-to-end

**PRD ref:** PRD-v5-MVP §10 M0 pre-M1 task PR-2
**Blocks:** M2-17 (CI dual-stack finalize) — builds on this.
**[PARALLEL WITH: M0-06]**
**Estimated:** 1 workflow YAML, ~2–3 days.

**Context.** PRD calls for GitHub Actions matrix `windows-latest` × net6/8 × x64,
running the existing `IntegrationTestFixture` end-to-end. This pays back
throughout M1+M2 because every PR starts getting integration coverage.

**Files to create/edit**

- `/c/work/snoopwpf/.github/workflows/agent-ci.yml`

**Steps**

1. Job `agent-build-test` on `windows-latest`, matrix over
   `target: [net8.0-windows, net6.0-windows]` (integration tests run on net8;
   solution still builds all targets).
2. Steps: `actions/checkout@v4` → `actions/setup-dotnet@v4` with
   `global.json` → `dotnet build Snoop.sln` → `dotnet test Snoop.Core.Tests`
   → `dotnet test SnoopWPF.Agent.Tests` → `dotnet test
   SnoopWPF.Agent.IntegrationTests` → upload test logs on failure.
3. Concurrency: `concurrency: { group: agent-ci-${{ github.ref }},
   cancel-in-progress: true }`.
4. Badge in `README.md` linking the workflow.

**Acceptance criteria**

```bash
test -f /c/work/snoopwpf/.github/workflows/agent-ci.yml
# Minimum content checks
grep -q "windows-latest" /c/work/snoopwpf/.github/workflows/agent-ci.yml
grep -q "SnoopWPF.Agent.IntegrationTests" /c/work/snoopwpf/.github/workflows/agent-ci.yml
# Push the branch and confirm the run is green before closing the bead —
# document the run URL in the commit body.
```

**Commit**

```
ci(M0-07): PR-2 headless CI wiring — matrix net6/8 on windows-latest

Runs Snoop.Core.Tests + SnoopWPF.Agent.Tests + SnoopWPF.Agent.IntegrationTests
against the integration harness built for v3. First green run: <URL>.
Pre-M1 de-risk per PRD-v5-MVP §10 PR-2.
```

---

# Phase M1 — Foundation + Hardening

> PRD §10 M1. 22 beads implementing SessionPolicy, [Sensitive]+MF-10, MF-11,
> WpfLocator, state-delta schema, IIdlingResource contract, HMAC audit writer,
> IDeterministicInputStrategy scaffold, analyzers. M1 closes when all v3
> integration tests are still green AND S-3b circular-dep test is green AND
> every M1 bead lands.
>
> **Gate sequence** inside M1 is serial; parallelization markers only appear on
> specific analyzer/doc beads that don't touch the selector/redaction/locator
> core.

## M1-01: SessionPolicy + SessionMode + InputTier

> **UPDATED 2026-04-16** — `SessionMode` enum must include a third
> value `Brokered` (between `CoLocated = 0` and `Injection = 2`).
> `SessionPolicy.Create(Brokered, opts)` behaves identically to
> `CoLocated` for policy (owned app, caller-chosen redaction).
> MF-11 redaction-forcing applies to `Injection` only. See
> `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md`.

**PRD ref:** PRD §4.3 + FD-2 above.
**Blocks:** M1-02 through M1-20.
**Estimated:** 3 new files + edits to SnoopAgentOptions, ~180 LOC.

**Context.** SessionPolicy is the single source of truth for mode, tier, and
gate flags. Everything downstream reads from it. Making it immutable early
prevents the v4 C2 class of bug where tools checked options mid-call.

**Files to create/edit**

- **Create** `SnoopWPF.Agent.Contracts/SessionPolicy.cs` — record per FD-2.
- **Create** `SnoopWPF.Agent.Contracts/SessionMode.cs` — enum.
- **Create** `SnoopWPF.Agent.Contracts/InputTier.cs` — enum `L0ReadOnly | L0 | L1`.
- **Edit** `SnoopWPF.Agent.Contracts/SnoopAgentOptions.cs` (or
  `SnoopWPF.Agent.Server/SnoopAgentOptions.cs` — wherever options live today)
  to add `MaxTier`, `EnableAutomation`, `EnableRedaction`, `AllowSensitiveRetention`
  properties. Do NOT remove existing `EnableMutation` (already present).
- **Edit** `SnoopWPF.Agent.Server/SnoopAgent.cs`: at `StartCoLocated` / injection
  entry, construct `SessionPolicy.Create(mode, opts)` once and pass down.

**Pre-flight verification**

```bash
grep -n "EnableMutation" /c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopAgent.cs
grep -rn "SessionPolicy" /c/work/snoopwpf/ 2>&1 | head -5   # expect zero hits
# Find all call sites for the existing SnoopAgent.Start method to update in step 0 below
grep -rn "SnoopAgent\.Start(" /c/work/snoopwpf/ --include="*.cs"
```

**Steps**

0. **Rename `SnoopAgent.Start(...)` to `SnoopAgent.StartCoLocated(...)`** and
   update all call sites (use the pre-flight grep above to find them). Keep the
   original `Start()` method as a one-release compatibility shim:
   ```csharp
   [Obsolete("Use StartCoLocated. This overload will be removed in v2.0.")]
   public static SnoopAgentHandle Start(SnoopAgentOptions? options = null)
       => StartCoLocated(options);
   ```
   This ensures existing samples and downstream consumers still compile for one
   release cycle without a hard break.
1. Write the three type files per FD-2.
2. `SessionPolicy.Create(mode, opts)`:
   - In injection mode: `EnableRedaction = true` (MF-11), `MaxTier = L0ReadOnly` (S7).
   - In co-located mode: pass through opts.
   - In brokered mode: pass through opts (identical to co-located for policy — owned app, caller owns redaction choice, full L1 available).
3. Add options fields with documented defaults (automation=false, mutation=false,
   redaction=true in MVP — safe by default).
4. Thread a `SessionPolicy` reference through `SnoopAgentHandle` → tool
   handlers. Do not touch tool-handler call-sites yet beyond adding the
   parameter; tools read the policy later (M1-04, M1-09, M1-15).
5. Unit tests in `SnoopWPF.Agent.Tests`:
   - `SessionPolicy.Create(Injection, opts).EnableRedaction == true` even when
     `opts.EnableRedaction == false` (MF-11).
   - `SessionPolicy.Create(Injection, opts).MaxTier == L0ReadOnly` (S7).
   - `SessionPolicy.Create(CoLocated, opts)` preserves `opts`.
   - `SessionPolicy.Create(Brokered, opts)` preserves `opts` (same as CoLocated).
   - MF-11 redaction-forcing test still targets `Injection` only; new test
     confirms `Brokered` respects caller redaction choice.

**Acceptance criteria**

```bash
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests --filter "FullyQualifiedName~SessionPolicy"
grep -q "SessionPolicy.Create(mode, " /c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopAgent.cs
```

**Commit**

```
feat(M1-01): SessionPolicy + SessionMode + InputTier contracts

Immutable per-session policy constructed once at session start. Enforces
MF-11 (injection mode forces EnableRedaction=true) and S7 (injection mode
caps MaxTier=L0ReadOnly) in the Create factory. Tests cover both invariants.
```

---

## M1-02: `[Sensitive]` attribute

**PRD ref:** PRD §9.2, FD-6.
**Depends on:** none (contracts-only).
**[PARALLEL WITH: M1-03]**
**Estimated:** 1 file, ~20 LOC.

**Context.** The attribute exists only so consumer code can opt properties
into structural redaction without needing a runtime-type table entry.
Rediscovered at every `Redact()` call via `GetCustomAttribute<SensitiveAttribute>()`.

**Files to create**

- `SnoopWPF.Agent.Contracts/SensitiveAttribute.cs` — per FD-6.

**Acceptance criteria**

```bash
test -f /c/work/snoopwpf/SnoopWPF.Agent.Contracts/SensitiveAttribute.cs
grep -q "AttributeUsage" /c/work/snoopwpf/SnoopWPF.Agent.Contracts/SensitiveAttribute.cs
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
```

**Commit**

```
feat(M1-02): [Sensitive] attribute in SnoopWPF.Agent.Contracts

Opt-in marker recognised by the structural-redaction map (M1-03). Applies to
property, field, class, struct, interface.
```

---

## M1-03: Structural redaction map (MF-10)

**PRD ref:** PRD §9.2 MF-10, global rule S3.
**Depends on:** M1-02 (`[Sensitive]` attribute).
**[PARALLEL WITH: none — touches RedactionFilter core]**
**Estimated:** 1 file edit, 1 new helper, ~120 LOC.

**Context.** PRD: property-name keyword match is insufficient. A
`ConnectionStringBuilder`-shaped DP named `DatabaseConfig` leaks credentials
via `ToString()`. `Redact()` must inspect the runtime type first.

**Files to edit/create**

- **Edit** `SnoopWPF.Agent.Engine/Infrastructure/RedactionFilter.cs`:
  - Add `private static readonly Type[] StructuralSensitiveRoots = { typeof(SecureString), typeof(NetworkCredential), typeof(DbConnectionStringBuilder) };`
  - Add `private static bool IsStructurallySensitive(object value)` —
    walks `value.GetType()` for assignability to any root OR a
    `[Sensitive]` attribute via `CustomAttributeData`.
  - In `Redact(...)`, short-circuit **before** invoking `ToString()` when
    `IsStructurallySensitive(value)` returns true.
- **Create** tests in `SnoopWPF.Agent.Tests/RedactionFilterStructuralTests.cs`:
  - `SqlConnectionStringBuilder` with embedded password → redacted
  - Custom `[Sensitive]`-marked type → redacted
  - `SecureString`, `NetworkCredential` → redacted
  - Innocuous string-typed DP named "Foo" → NOT redacted (control)

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~RedactionFilterStructural"
# MF-10 sentinel is present on the redact path
grep -q "IsStructurallySensitive\|StructuralSensitive" \
    /c/work/snoopwpf/SnoopWPF.Agent.Engine/Infrastructure/RedactionFilter.cs
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
```

**Commit**

```
feat(M1-03): MF-10 structural redaction map

RedactionFilter now inspects runtime type against {SecureString,
NetworkCredential, DbConnectionStringBuilder subtypes, [Sensitive]-marked
types} BEFORE invoking ToString(). Closes the ConnectionStringBuilder-
ToString leak path flagged in PRD-v5-MVP §9.2.
```

---

## M1-04: MF-11 injection-mode forced redaction enforcement (tool-side)

> **UPDATED 2026-04-16** — Clarify wording: MF-11 redaction-forcing
> applies to `Injection` mode only. `CoLocated` and `Brokered` pass
> through the caller's `EnableRedaction` choice unchanged (both are
> owned-app modes where the caller has compile-time control). No
> behavioural change; doc-level clarity only.

**PRD ref:** PRD §9.7 MF-11. Enforcement complements the factory-level override in M1-01.
**Depends on:** M1-01, M1-03.
**Estimated:** ~10 LOC edit + 1 test, 30 min.

**Context.** Factory already flips the flag. This bead adds a belt-and-braces
assertion in the `SnoopInspector.Redact` entry point that `EnableRedaction`
is true whenever `Mode == Injection`, with a clear `InvalidOperationException`
if violated. This catches downstream bugs that bypass the factory.

**Files to edit**

- `SnoopWPF.Agent.Engine/SnoopInspector.cs` (or wherever redaction dispatch
  lives) — at the top of the redaction-dispatch path, add:

  ```csharp
  if (sessionPolicy.Mode == SessionMode.Injection && !sessionPolicy.EnableRedaction)
      throw new InvalidOperationException(
          "Injection mode requires EnableRedaction=true (MF-11). Policy was constructed bypassing SessionPolicy.Create.");
  ```

**Tests**

- `SnoopWPF.Agent.Tests/InjectionRedactionEnforcementTests.cs`:
  - Fabricate a `SessionPolicy` (via `with { EnableRedaction = false, Mode = Injection }`)
    and attempt a property read. Expect `InvalidOperationException`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~InjectionRedactionEnforcement"
```

**Commit**

```
fix(M1-04): MF-11 belt-and-braces enforcement in SnoopInspector

Factory forces EnableRedaction=true for Injection mode; this adds a runtime
assertion at the redaction-dispatch entry so bypassing the factory fails
loudly instead of leaking DP values.
```

---

## M1-05: `WpfLocator` type + parser + analyzer integration

**PRD ref:** PRD §6, FD-1, bug #9 (NodeRegistry growth cap).
**Depends on:** M1-01 (SessionPolicy for Redaction-gated ViewModel locator).
**Estimated:** 4 new files + ~100 LOC parser, ~250 LOC total.

**Files to create**

- `SnoopWPF.Agent.Contracts/WpfLocator.cs` — per FD-1.
- `SnoopWPF.Agent.Contracts/WpfLocatorForm.cs` — enum.
- `SnoopWPF.Agent.Contracts/WpfLocatorParser.cs` — `public static WpfLocator Parse(string raw)`.
- `SnoopWPF.Agent.Tests/WpfLocatorParserTests.cs` — exhaustive per §6 form table.

**Steps**

1. Grammar:
   - `automationId=<value>` → `AutomationId`.
   - `viewModel=<typeShort>[, property=<p>, value=<v>]` → `ViewModel`.
   - `type=<typeShort>[, name=<n>]` → `TypeName`.
   - `path=<tokens separated by backslash>` → `Path`.
2. Reject unknown keys (return `null` + `LocatorParseException` at
   call-site). Cap raw length at 2048 chars.
3. Tests:
   - Every form in §6 parses round-trip.
   - Unknown keys throw.
   - Oversize strings throw.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~WpfLocatorParser"
```

**Commit**

```
feat(M1-05): WpfLocator type + parser

Four locator forms parsed (automationId, viewModel, type+name, path) per
PRD §6. Exhaustive round-trip tests. Integrates with SessionPolicy for
redaction-sensitive ViewModel resolution in M1-06.
```

---

## M1-06: `ISnoopInspector` — WpfLocator overloads with NodeRegistry growth cap

**PRD ref:** PRD §6 bullet "Resolution cost bounds" (bug #9); PRD §14 bug #9.
**Depends on:** M1-05.
**Estimated:** ~250 LOC + tests; interface + resolver.

**Context.** Every ISnoopInspector method that takes `nodeId` gains a
`WpfLocator` overload. The resolver caps new `NodeRegistry` entries at 100
per resolution; overshoot returns `LOCATOR_AMBIGUOUS`. Existing `nodeId`
overloads remain for compat (deprecated via analyzer in M1-17, not
`[Obsolete]`).

**Files to edit/create**

- **Edit** `SnoopWPF.Agent.Contracts/ISnoopInspector.cs`: add locator
  overloads for each existing nodeId method. Do not remove existing methods.
- **Create** `SnoopWPF.Agent.Engine/LocatorResolver.cs`: resolves a
  `WpfLocator` against the current tree; tracks new-NodeRegistry-entry
  count per-call; throws `LocatorAmbiguousException` at cap.
- **Edit** `SnoopWPF.Agent.Engine/SnoopInspector.cs`: implement the new
  interface overloads by delegating to `LocatorResolver` then calling the
  existing nodeId path.
- **Fix** `InspectElementDto.ParentNodeId` hardcoded empty string at
  `SnoopInspector.cs:462` (PRD §14 debt item). Parent resolution is now
  meaningful because locators can walk ancestors.
- **Edit** `SnoopWPF.Agent.IntegrationTests/TestWpfApp.cs` — add a
  `VirtualizingStackPanel`-backed `ListBox` named `testBigList` with
  `ItemsSource = Enumerable.Range(0, 10000).Select(i => $"Item {i}")` and
  `x:Name="testBigList"` (or the equivalent code-behind `Name` assignment).
  This fixture is required by the virtualized-list locator test below and
  by M2-04b.

**Tests**

- `SnoopWPF.Agent.IntegrationTests/LocatorResolutionTests.cs`:
  - Each of the four forms resolves on `TestWpfApp`.
  - Virtualized list with 10,000 items and a `viewModel=` locator: resolver
    returns `LOCATOR_AMBIGUOUS` at the 100-entry cap (NOT OOM).
  - `path=` with two sibling buttons matches first (documented behaviour);
    analyzer SWPF0011 warns separately in M1-18.
  - `ParentNodeId` is non-empty for non-root elements.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~LocatorResolution"
# Cap is implemented
grep -q "100\|cap\|Cap" /c/work/snoopwpf/SnoopWPF.Agent.Engine/LocatorResolver.cs
# ParentNodeId no longer hard-coded empty
! grep -q 'ParentNodeId.*string.Empty' /c/work/snoopwpf/SnoopWPF.Agent.Engine/SnoopInspector.cs
```

**Commit**

```
feat(M1-06): ISnoopInspector WpfLocator overloads + NodeRegistry growth cap

All nodeId-bearing methods gain locator overloads. LocatorResolver caps new
NodeRegistry entries at 100 per resolution; overshoot surfaces
LOCATOR_AMBIGUOUS (bug #9). InspectElementDto.ParentNodeId now resolved via
parent chain, no longer hard-coded empty.
```

---

## M1-07: `wpf_find_elements` returns `hasCommandBinding`

**PRD ref:** PRD §5.1 W3-C2, FD-3 (FailureReason depends on this signal for
`CannotExecuteCommand` remediation).
**Depends on:** M1-06.
**Estimated:** ~60 LOC tool edit + ~40 LOC test.

**Context.** Agents pick L0 `wpf_execute_command` vs L1 `wpf_click` based on
this bool. Saves one `inspect_element` round-trip per act — critical for the
§11 metric "one act call within 3 tool calls".

**Files to edit/create**

- **Edit** `SnoopWPF.Agent.Contracts/Dtos/FindElementResultDto.cs` — add
  `[DataMember(Name = "hasCommandBinding")] public bool HasCommandBinding { get; init; }`.
- **Edit** `SnoopWPF.Agent.Engine/SnoopInspector.cs` `FindElements`
  implementation: for each hit, check if `Command` DP is set non-null via
  `BindingOperations.GetBindingExpression` or `GetLocalValue`.
- **Edit** `SnoopWPF.Agent.Tools` — surface new field in the tool-handler
  response.
- **Test** integration: find a `Button` with `Command` bound → field true;
  find a `Button` without binding → false.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~HasCommandBinding"
grep -q "hasCommandBinding" /c/work/snoopwpf/SnoopWPF.Agent.Contracts/Dtos/FindElementResultDto.cs
```

**Commit**

```
feat(M1-07): wpf_find_elements returns hasCommandBinding

Enables the 3-call agent bootstrap chain (get_session_info → find_elements →
execute_command|click) without an intermediate inspect_element. Closes PRD
§5.1 W3-C2.
```

---

## M1-08: `wpf_get_session_info` inlines top-level windows

**PRD ref:** PRD §5.1 W3-C2, §11 metric #1.
**Depends on:** M1-06 (emits locators).
**Estimated:** ~80 LOC, ~30 min.

**Context.** Returning windows inline means the canonical bootstrap is 3
calls, not 4. Field is additive; `wpf_get_windows` remains for explicit
refresh.

**Files to edit**

- `SnoopWPF.Agent.Contracts/Dtos/SessionInfoDto.cs` — add
  `[DataMember(Name = "windows")] public List<WindowSummaryDto> Windows { get; init; } = new();`.
- `SnoopWPF.Agent.Engine/SnoopInspector.GetSessionInfo` — populate from
  `Application.Current.Windows`, projecting to `WindowSummaryDto` with
  locator + dimensions.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SessionInfoWindows"
# Response size-watch (must stay under an obvious regression threshold)
```

**Commit**

```
feat(M1-08): get_session_info inlines top-level windows

Supports the 3-call canonical bootstrap chain (PRD §11 metric #1). Existing
wpf_get_windows remains for post-bootstrap refresh.
```

---

## M1-09: Canonical `StateDelta` schema wired into `wpf_set_property`

**PRD ref:** PRD §7, FD-3. `wpf_set_property` is the only mutation tool in
v3 and becomes the pattern for all M2 act tools.
**Depends on:** M1-05, M1-06.
**Estimated:** ~200 LOC + tests.

**Files to edit/create**

- **Create** `SnoopWPF.Agent.Contracts/Dtos/StateDeltaDto.cs` — per FD-3.
- **Create** `SnoopWPF.Agent.Contracts/FailureReason.cs` — enum per FD-3.
- **Create** `SnoopWPF.Agent.Contracts/Dtos/SuggestionDto.cs` — per FD-3.
- **Edit** `SnoopWPF.Agent.Tools/SetPropertyTool.cs` — return
  `StateDeltaDto` instead of current opaque response. Populate
  `previousValue` pre-call, `stateChanged` deferred to serialization-time
  (M1-11).
- **Tests** end-to-end: success path, failure path, one success-but-unchanged
  path returning `StateUnchanged` (FailureReason=7) + suggestion
  `wpf_inspect_element`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SetPropertyStateDelta"
```

**Commit**

```
feat(M1-09): StateDelta schema on wpf_set_property

Canonical post-action response (§7) — pattern for all M2 act tools.
Populates previousValue pre-call; stateChanged deferred to M1-11
serialization-time rule.
```

---

## M1-10: `FailureReason` enum coverage + suggestion machinery

**PRD ref:** PRD §7.4 (13-value enum), §7.5 (suggestion schema).
**Depends on:** M1-09.
**Estimated:** 1 helper file + tests, ~130 LOC.

**Context.** PRD §7.4 table defines 13 triggers and their suggested remediation
tool. This bead writes the `FailureReasonDescriptor` helper that maps a
`FailureReason` value to a `SuggestionDto` with machine-executable args.
Prose suggestions are not permitted.

**Files to create**

- `SnoopWPF.Agent.Engine/StateDelta/FailureReasonDescriptor.cs`:
  ```csharp
  internal static SuggestionDto? Suggest(FailureReason reason, WpfLocator? ctx)
  {
      return reason switch
      {
          FailureReason.ElementNotFound => new SuggestionDto { Tool = "wpf_find_elements", Args = … },
          FailureReason.ElementNotVisible => new SuggestionDto { Tool = "wpf_wait_for_property", Args = { (locator,ctx), (propertyName,"IsVisible"), (expectedValue,"true"), (timeoutMs,"5000") } },
          // … 10 more
          FailureReason.AutomationDisabled => null,
          FailureReason.MutationDisabled => null,
          FailureReason.TargetNotRunning => new SuggestionDto { Tool = "broker_launch_target", Args = new() },
          _ => null,
      };
  }
  ```
- Tests: every enum value round-trips to the PRD §7.4 table entry.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~FailureReasonDescriptor"
```

**Commit**

```
feat(M1-10): FailureReason → Suggestion machinery

All 13 PRD §7.4 enum values map to machine-executable SuggestionDto with
tool+args. Null for AutomationDisabled/MutationDisabled (session reconfig
required — no auto-remediation). TargetNotRunning maps to broker_launch_target.
```

---

## M1-11: `stateChanged` computed at response-serialization time

**PRD ref:** PRD §7.3 W3-C1.
**Depends on:** M1-09.
**Estimated:** 1 serializer hook + 1 test, ~80 LOC.

**Context.** Re-entrant `PropertyChangedCallback` reverting a value can make
`stateChanged` true at SetValue-call time but false by serialize-time. PRD:
compute at response-serialize by comparing current observable state to
`previousValue`.

**Files to edit**

- `SnoopWPF.Agent.Engine/StateDelta/StateDeltaSerializationHook.cs` — new
  helper invoked in `SnoopInspector` just before returning the DTO. Reads
  the captured `previousValue`, reads the observable state now, compares.
- `SnoopWPF.Agent.Tests/StateChangedSerializationTests.cs` — DP with a
  reverting `PropertyChangedCallback` → `stateChanged: false` despite the
  set succeeding.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~StateChangedSerialization"
```

**Commit**

```
feat(M1-11): stateChanged computed at response-serialization time

Closes the reverting-PropertyChangedCallback false-positive (PRD §7.3 W3-C1).
Test fabricates a DP whose coerce reverts the value and asserts
stateChanged=false despite SetValue succeeding.
```

---

## M1-12: `IIdlingResource` contract + 4 built-in resources

**PRD ref:** PRD §8.2, FD-4.
**Depends on:** M1-01 (policy), M0-03 (S-3 verdict).
**Estimated:** ~400 LOC; 1 interface + 4 resource types + registry + tests.

**Files to create**

- `SnoopWPF.Agent.Contracts/IIdlingResource.cs` — per FD-4.
- `SnoopWPF.Agent.Contracts/IdleChangedEventArgs.cs` — sealed class.
- `SnoopWPF.Agent.Engine/Sync/IdlingResourceRegistry.cs` — thread-safe list
  of registered resources; AND-gate evaluation via `Dispatcher.InvokeAsync`
  at `ContextIdle`.
- `SnoopWPF.Agent.Engine/Sync/DispatcherIdlingResource.cs`.
- `SnoopWPF.Agent.Engine/Sync/CompositionRenderingResource.cs`.
- `SnoopWPF.Agent.Engine/Sync/StoryboardResource.cs`.
- `SnoopWPF.Agent.Engine/Sync/DispatcherTimerResource.cs`.

**Steps**

1. Each resource hooks the relevant source (Storyboard via
   `Timeline.CurrentStateInvalidated`; timers via tracking
   `DispatcherTimer.IsEnabled` through reflection OR expose only what
   `System.Windows.Threading.DispatcherTimer` provides publicly).
2. `CheckPriority` returns `DispatcherPriority.ContextIdle` for all four
   (PRD §8.2 W3-H1).
3. Registry evaluation: `IsIdle = resources.All(r => r.IsIdle)`, raises
   aggregated `IdleChanged`.
4. Tests: each resource goes idle/non-idle correctly under synthetic loads;
   AND-gate waits for the slowest.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~IdlingResource|FullyQualifiedName~IdleGate"
```

**Commit**

```
feat(M1-12): IIdlingResource contract + 4 built-ins (Detox pattern)

Dispatcher, CompositionRendering, Storyboard, DispatcherTimer resources;
AND-gate registry at ContextIdle priority per PRD §8.2 W3-H1. Foundation
for M2-11 wpf_pump_until_idle.
```

---

## M1-13: HMAC audit-log writer (Channel\<AuditEntry\>)

**PRD ref:** PRD §9.4 bug #10, FD-7, global rule S5.
**Depends on:** M1-01 (SessionPolicy provides session id).
**Estimated:** ~300 LOC; 1 writer + 1 DTO + tests.

**Files to create**

- `SnoopWPF.Agent.Contracts/Audit/AuditEntry.cs` — per FD-7 (internal).
- `SnoopWPF.Agent.Engine/Audit/AuditLogWriter.cs`:
  - Background `Task` reading a `Channel<AuditEntry>`.
  - Session key: `RandomNumberGenerator.GetBytes(32)`.
  - `counterNonce`: `Interlocked.Increment`.
  - HMAC: `HMACSHA256` over `entryJson || prevHmac || sessionKey || counterNonce`.
  - File path: `%LOCALAPPDATA%\SnoopWPF\audit\{sessionId}.jsonl`.
  - Owner-only ACL on the file (net8 uses `FileSystemAclExtensions`).
- `SnoopWPF.Agent.Tests/AuditLogChainTests.cs`:
  - 1,000 writes produce a monotonic-seq file.
  - HMAC chain is recomputable from session key + file contents.
  - Tampering any entry breaks the chain (assert by mutating byte N).
  - `reason` field with embedded newlines is sanitized (newlines replaced,
    `\0` dropped, 256-char cap).

**Brokered-mode audit log scope**: in Brokered mode the audit log is
**target-only**. The broker process does not write audit entries. This
avoids HMAC chain collision on `{sessionId}.jsonl` when broker and target
would otherwise race to write to the same file. The M2-21 acceptance test
asserts the broker's `BrokerHost` never constructs an `AuditLogWriter`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~AuditLogChain"
```

**Commit**

```
feat(M1-13): HMAC audit writer on Channel<AuditEntry>

Single background writer holds chain state; HMAC-SHA256 over entryJson ||
prevHmac || sessionKey || counterNonce; session key from RNG, not process
identity. Tests cover monotonic seq, chain recomputation, tamper detection,
reason sanitization. Brokered-mode audit is target-only (broker does not
write entries — avoids HMAC chain collision). Closes PRD §9.4 bug #10.
```

---

## M1-14: `SnoopWPF.Agent.Input.Deterministic` project scaffold

**PRD ref:** PRD §4.5.
**Depends on:** M1-01.
**Estimated:** 1 .csproj + 1 placeholder class, ~30 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/SnoopWPF.Agent.Input.Deterministic.csproj`
  — TFM `net8.0-windows`, references `SnoopWPF.Agent.Contracts`,
  `SnoopWPF.Agent.Engine`.
- `SnoopWPF.Agent.Input.Deterministic/AssemblyInfo.cs` —
  `InternalsVisibleTo("SnoopWPF.Agent.Tests")`.
- **Edit** `/c/work/snoopwpf/Snoop.sln` — add project (use `dotnet sln add`).

**Acceptance criteria**

```bash
dotnet.exe sln /c/work/snoopwpf/Snoop.sln list | grep "SnoopWPF.Agent.Input.Deterministic"
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
```

**Commit**

```
feat(M1-14): SnoopWPF.Agent.Input.Deterministic project scaffold

Empty project; strategies land in M1-15 (selector) and M2-01..07 (tools).
net8.0-windows only; multi-target deferred to v2.0 with L3/L4.
```

---

## M1-15: `IDeterministicInputStrategy` + `InputStrategySelector`

**PRD ref:** PRD §4.4, FD-5, global rule S2 (strategy-layer gating).
**Depends on:** M1-01, M1-14.
**Estimated:** ~300 LOC; 2 contract types + selector + tests.

**Files to create**

- `SnoopWPF.Agent.Contracts/IDeterministicInputStrategy.cs` — per FD-5.
- `SnoopWPF.Agent.Contracts/InputIntent.cs` — record `{ Kind, Arguments }`
  where `Kind` is an enum of act-tool intents (Click, Toggle, ExpandCollapse,
  SetCheckState, SetTextValue, SelectItem, ExecuteCommand, SetProperty).
- `SnoopWPF.Agent.Contracts/Dtos/DeterministicInputResult.cs` — per FD-5.
- `SnoopWPF.Agent.Input.Deterministic/InputStrategySelector.cs`:
  - Takes `SessionPolicy` + registered strategies.
  - `Select(intent, target)` returns best-matching strategy under tier/
    gate constraints; returns null + `FailureReason.TierMismatch` /
    `AutomationDisabled` / `MutationDisabled` as appropriate.
  - **Refuses L0/L1 strategies in Injection mode** (S7).
  - **No tool handlers bypass this selector** — verified by analyzer in M1-16/17.
- Tests:
  - SessionPolicy.Mode=Injection rejects every strategy.
  - Mutation=false rejects SetProperty intent.
  - Automation=false rejects Click/Toggle/ExpandCollapse.
  - MaxTier=L0 rejects L1 strategies.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~InputStrategySelector"
```

**Commit**

```
feat(M1-15): IDeterministicInputStrategy + InputStrategySelector

All tier/gate enforcement lives here per global rule S2. Injection mode
refuses L0/L1 strategies (S7). Tests cover Mutation/Automation/MaxTier/Mode
refusal paths. Act tools (M2-01..07) plug their strategies into this
selector.
```

---

## M1-16: Roslyn analyzer `SWPF0001` — `Console.Write*` in MCP entrypoint

> **UPDATED 2026-04-16** — Analyzer attribute name is `[McpStdioEntrypoint]`
> (single attribute, no mode parameter). The analyzer fires on any project
> that owns the MCP stdio anchor: CoLocated targets, Brokered brokers (e.g.
> MC's `UiMcpHost`), and Injection hosts (`snoop-mcp.exe`). The analyzer
> does NOT fire on Brokered targets (e.g. MC itself) — they own their own
> stdout and `Console.Write*` is harmless log spam. Note: the broker-side
> spawn contract (M2-21) is the belt-and-braces guarantee that even if a
> Brokered target does `Console.Write`, it never corrupts MCP stdio (because
> the broker drains/discards the target's stdout via `RedirectStandardOutput`).

**PRD ref:** PRD §9.5, global rule S6.
**Depends on:** none strict (can parallel with M1-17/M1-18).
**[PARALLEL WITH: M1-17, M1-18]**
**Estimated:** 1 analyzer project, ~200 LOC.

**Context.** Consumer apps marked with `[McpStdioEntrypoint]` (ships in
`Contracts`) must not call `Console.Write*` — stdout is claimed by the MCP
transport. The analyzer catches this at build time. CoLocated targets,
Brokered brokers, and Injection hosts all apply this attribute. Brokered
targets do not (they do not own the MCP stdio stream).

**Files to create/edit**

- **Create** `SnoopWPF.Agent.Analyzers/SnoopWPF.Agent.Analyzers.csproj` —
  Microsoft.CodeAnalysis.CSharp reference; analyzer + code-fix.
- **Create** `SnoopWPF.Agent.Contracts/McpStdioEntrypointAttribute.cs`
  (attribute name is `[McpStdioEntrypoint]`, no mode parameter).
- **Create** `SnoopWPF.Agent.Analyzers/Console0001Analyzer.cs` +
  `Console0001CodeFix.cs`.
- **Edit** `SnoopWPF.Agent.Server/SnoopWPF.Agent.Server.csproj` — analyzer
  package reference so consumers pull it with the server NuGet.
- Tests in `SnoopWPF.Agent.Analyzers.Tests` (new project):
  - Console.Write in `[McpStdioEntrypoint]`-marked entry → diagnostic.
  - Console.Write in non-marked code → no diagnostic.
  - Code-fix replaces with `Trace.TraceInformation`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Analyzers.Tests
# Analyzer package id registered in Server csproj
grep -q "SnoopWPF.Agent.Analyzers" /c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopWPF.Agent.Server.csproj
```

**Commit**

```
feat(M1-16): SWPF0001 analyzer — no Console.Write in [McpStdioEntrypoint]

Build-time catch for the stdout-contention class of bug (PRD §9.5). Ships
with SnoopWPF.Agent.Server so consumers get it automatically. Code fix
rewrites to Trace.TraceInformation.
```

---

## M1-17: Roslyn analyzer `SWPF0010` — Stored `nodeId` warning

**PRD ref:** PRD §6 ("analyzer SWPF0010 warns on hard-coded nodeIds").
**[PARALLEL WITH: M1-16, M1-18]**
**Estimated:** ~150 LOC; analyzer only, no code-fix (user must decide which locator).

**Context.** NodeIds are session-scoped; persisting them to a field or
collection across tool calls will silently break on reconnect. Analyzer
warns on storage patterns: field assignment, list append, dictionary value
where the expression type is `string` and the symbol comes from a known
`nodeId`-returning API.

**Files to create**

- `SnoopWPF.Agent.Analyzers/NodeId0010Analyzer.cs`.
- Tests.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Analyzers.Tests \
    --filter "FullyQualifiedName~NodeId0010"
```

**Commit**

```
feat(M1-17): SWPF0010 analyzer — stored nodeId warning

Catches persistent storage of session-scoped nodeIds. Consumers store
WpfLocator instead per PRD §6.
```

---

## M1-18: Roslyn analyzer `SWPF0011` — Stored `path=` locator warning

**PRD ref:** PRD §6 W3-H3 (path= is NOT durable under sibling reordering).
**[PARALLEL WITH: M1-16, M1-17]**
**Estimated:** ~130 LOC.

**Context.** Three sibling Buttons in a StackPanel all match the same
`path=` locator. Storing such a locator is fragile. Analyzer warns when a
string literal matching `path=` is stored (field init, const, array literal,
dict value).

**Files to create**

- `SnoopWPF.Agent.Analyzers/PathLocator0011Analyzer.cs`.
- Tests.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Analyzers.Tests \
    --filter "FullyQualifiedName~PathLocator0011"
```

**Commit**

```
feat(M1-18): SWPF0011 analyzer — stored path= locator warning

Flags fragile path= locator storage per PRD §6 W3-H3. Prefer automationId=
or viewModel= forms for persistence.
```

---

## M1-19: `Console.Out` takeover in `StartCoLocated`

> **UPDATED 2026-04-16** — Still valid as written (applies only to
> `StartCoLocated`). Add a unit test asserting `StartBrokered` does
> NOT touch `Console.Out` — the broker owns MCP stdio, the target
> owns its own stdout. See new bead M1-21 for `StartBrokered` API.

**PRD ref:** PRD §9.5, global rule S6.
**Depends on:** M1-16 (analyzer pairing; attribute defined there).
**Estimated:** ~15 LOC edit + 1 test.

**Context.** First statement of `SnoopAgent.StartCoLocated` must be
`Console.SetOut(TextWriter.Null)`. PRD §14 bugs 1–2 were review-fix-wave
closed already; this bead formalises the requirement and adds a regression
test that reverts if the redirect is not in place.

**Files to edit**

- `SnoopWPF.Agent.Server/SnoopAgent.cs` — `StartCoLocated`: line 1 must be
  `Console.SetOut(TextWriter.Null);`. (If already present, add a comment
  `// M1-19: stdout held by MCP transport; see SWPF0001.` pinning it.)
- `SnoopWPF.Agent.IntegrationTests/StdoutTakeoverTests.cs` — invokes
  `StartCoLocated`, then `Console.WriteLine("X")`, then checks that no "X"
  leaked to the MCP transport's stdout.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~StdoutTakeover"
head -n 30 /c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopAgent.cs | \
    grep -q "Console.SetOut(TextWriter.Null)"
```

**Commit**

```
fix(M1-19): pin Console.Out takeover as first statement of StartCoLocated

Regression guard for the v3 review-fix wave bugs 1-2. Paired with analyzer
SWPF0001 from M1-16.
```

---

## M1-20: `UnsafeAccessor` self-test + HwndSource headless precondition

**PRD ref:** PRD §4.2 boot sequence step 5.
**Depends on:** M1-01.
**[PARALLEL WITH: none — final M1 item; closes M1 gate]**
**Estimated:** ~90 LOC; 1 self-test + 1 precondition + tests.

**Files to edit/create**

- **Edit** `SnoopWPF.Agent.Server/SnoopAgent.cs`:
  - After `Console.SetOut` (M1-19), before MCP loop start, call:
    - `SelfTest.UnsafeAccessorBindings()` — on net8, resolve the one or two
      private-member accessors MVP uses (if any; otherwise no-op but log a
      diagnostic so v2.0 L3 work has a smoke test).
    - `SelfTest.HwndSourcePresent()` — ensures `HwndSource.FromVisual` works
      against `Application.Current.MainWindow`. If headless without a window
      yet, register a one-shot `Application.Current.Activated` that fails
      the agent if no window appears within 10s.
- **Create** `SnoopWPF.Agent.Server/SelfTest.cs`.
- Tests in `SnoopWPF.Agent.IntegrationTests/SelfTestTests.cs`:
  - Self-test passes in normal WPF flow.
  - Self-test fails with a specific error code when launched without any
    window after timeout.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SelfTest"
```

**M1 GATE** — all of the following must pass before M2 opens:

```bash
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Debug -p:TreatWarningsAsErrors=true
dotnet.exe test /c/work/snoopwpf/Snoop.Core.Tests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.InjectionTests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Analyzers.Tests
# S-3b spike still green
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~PollChangesWithoutWaitForProperty"
```

**Commit**

```
feat(M1-20): UnsafeAccessor + HwndSource startup self-test

Boot-sequence step 5 (PRD §4.2). Adds smoke tests for the two
precondition classes that MVP and future L3 rely on.
```

---

## M1-21: `SnoopAgent.StartBrokered(app, pipeName, sessionTokenHex, opts)` API

> **NEW 2026-04-16** per architecture change. Read
> `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md` first.

**PRD ref:** PRD §4.1 Brokered mode, §4.2 brokered boot sequence, §9.7 Brokered pipe hardening.
**Depends on:** M1-01, M1-22.
**Estimated:** ~280 LOC + unit/integration tests.

**API signature**

```csharp
public static SnoopAgentHandle StartBrokered(
    Application app,
    string pipeName,
    string sessionTokenHex,
    SnoopAgentOptions opts);
```

**Required behaviour**

- Open `NamedPipeServerStream(pipeName, PipeDirection.InOut, maxAllowedInstances=1,
  PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)`.
- Perform the same `PerformPipeHandshakeAsync` pattern as
  `McpServerSetup.RunWithPipeAsync`: 5-second timeout,
  `CryptographicOperations.FixedTimeEquals` token compare,
  `sessionTokenHex` is the expected token.
- **Reconnect loop**: after the client disconnects, dispose the old
  `NamedPipeServerStream`, recreate it with the same options, then call
  `WaitForConnectionAsync` again. Loop indefinitely until `SnoopAgent.Stop()`
  is called or the process exits. This supports broker crash-and-restart
  without requiring a target restart.
- `Console.Out` is NOT touched — the target owns its own stdout; the broker
  (separate process) owns the MCP stdio anchor.

**Files to create/edit**

- `SnoopWPF.Agent.Server/SnoopAgent.cs` — add `StartBrokered(app, pipeName, sessionTokenHex, opts)`.
- `SnoopAgent.Stop()` (if not already present) — drains pending requests and
  closes the pipe gracefully so the broker sees a clean disconnect and the
  reconnect loop terminates.

**Tests**

- Unit test: mock-broker round-trip — broker client connects, sends a framed
  request, receives a framed response. Assert `Console.Out` is untouched
  (contrast with `StartCoLocated` which takes it over per M1-19).
- Reconnect test: broker disconnects mid-session, re-connects, next tool call
  succeeds (verifies the reconnect loop).
- Redaction target-side test: a `[Sensitive]`-marked DP's value arrives at the
  mock broker as the redaction sentinel, not the raw value.
- Negative test: client connecting without valid token → handshake rejection
  (constant-time compare, no timing leak).
- Negative test: connecting from a different Windows user account →
  `PipeOptions.CurrentUserOnly` refusal (pipe open fails at OS level).

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~StartBrokered"
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~BrokeredRoundTrip"
```

**Commit**

```
feat(M1-21): SnoopAgent.StartBrokered — hardened named-pipe transport

PipeOptions.CurrentUserOnly + 256-bit token handshake (PerformPipeHandshakeAsync
pattern). Reconnect loop supports broker crash/restart. Console.Out untouched
(broker owns MCP stdio). Explicit sessionTokenHex parameter required.
```

---

## M1-22: Brokered-mode pipe framing

> **NEW 2026-04-16** per architecture change. Read
> `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md` first.

**PRD ref:** PRD §4.1, §4.2. Reuses `SnoopWPF.Agent.Remote`
framing from v3 if that project already provides request-ID
correlation + in-order guarantees; otherwise adds them.
**Depends on:** none (pure framing library work; can run first in M1 phase).
**Estimated:** ≤300 LOC including tests (split if larger).

**Pre-flight command**

```bash
grep -n "class FramedJsonTransport" /c/work/snoopwpf/SnoopWPF.Agent.Remote/*.cs
```

If the class exists, the bead scope is: audit the class, add an in-order-delivery
guarantee test, add clean-disconnect handling, and expose the framing publicly
(change `internal` to `public`) so `StartBrokered` (M1-21) can consume it
without a cross-project workaround. If the class does not exist, scope is:
implement it from scratch per the frame schema below.

**Files to create/edit**

- `SnoopWPF.Agent.Remote/FramedJsonTransport.cs` — frame format
  (`{ requestId, method, params | result | error }`), in-order delivery
  guarantee, clean disconnect handling. Make class `public` so M1-21 and
  M2-21 can reference it.
- Unit tests covering: normal request/response, malformed frames,
  client disconnect mid-request, server disconnect mid-response,
  in-order delivery assertion.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~RemoteFraming"
```

**Commit**

```
feat(M1-22): brokered-mode pipe framing in SnoopWPF.Agent.Remote

Request-ID correlation, in-order guarantee, clean disconnect.
FramedJsonTransport made public. Consumed by StartBrokered (M1-21)
and external brokers (M2-21). ≤300 LOC.
```

---

# Phase M2 — Act + Extract + Sync + Integration

> PRD §10 M2. Beads span L0/L1 act tools, one extract tool, three sync
> utilities, VeriGUI harness, CI dual-stack, NuGet packaging,
> brokered-mode consumer deliverables, broker scaffolding. Target
> 6–8 weeks serialized. (M2-13 / M2-14 shim beads removed per
> 2026-04-16 architecture change.)

## M2-01: `wpf_execute_command` (L0)

**PRD ref:** PRD §5.2 row 10, Guidelines/Limitations/Applies-to block.
**Depends on:** M1-15, M1-10, M1-11.
**Estimated:** ~220 LOC; 1 strategy + 1 tool + tests.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/ExecuteCommandStrategy.cs`:
  - `Tier = L0`.
  - Resolves `ICommand` via `Command` DP.
  - Gates on `EnableMutation` (per S2).
  - Checks `CanExecute` → `CannotExecuteCommand` on false.
  - Invokes `Execute()`.
  - Captures `previousValue` = element routed-event count + window-count
    delta.
- `SnoopWPF.Agent.Tools/ExecuteCommandTool.cs` — MCP tool wrapper.
- Tool description with PRD §5.2 Guidelines / Limitations / Applies-to
  (ships in `tools/list` per W3-E1).
- Tests:
  - Button with Command bound + CanExecute=true → success, stateChanged
    reflects window-count delta if any.
  - Button with Command bound + CanExecute=false → `CannotExecuteCommand`
    + suggestion `wpf_resolve_binding`.
  - Button without Command → `PATTERN_NOT_SUPPORTED`.
  - Mutation disabled → `MutationDisabled` from selector.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~ExecuteCommand"
# Tool description includes all four W3-E1 components
grep -qE "Guidelines|Limitations|Applies to" /c/work/snoopwpf/SnoopWPF.Agent.Tools/ExecuteCommandTool.cs
```

**Commit**

```
feat(M2-01): wpf_execute_command (L0) with state-delta and description block

PRD §5.2 row 10. L0 shortcut — resolves Command DP, CanExecute gate, Execute.
Includes Guidelines/Limitations/Applies-to per W3-E1. Closes the
'agent picked L1 click when L0 existed' failure class.
```

---

## M2-02: `wpf_set_text_value` (L0)

**PRD ref:** PRD §5.2 row 11, Applies to {TextBox, PasswordBox, RichTextBox}.
**Depends on:** M2-01.
**Estimated:** ~200 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/SetTextValueStrategy.cs`:
  - `Tier = L0`.
  - For `TextBox`: `SetValue(TextBox.TextProperty, value)`.
  - For `PasswordBox`: `SetValue(PasswordBox.PasswordProperty, value)`.
    Treats `value` as `SensitiveText` (S3); redacted in any logging path.
    Opt-out via call option `retainSensitive=true` only when
    `AllowSensitiveRetention=true` in policy.
  - For `RichTextBox`: flow-document plaintext.
- `SnoopWPF.Agent.Tools/SetTextValueTool.cs`.
- Tests: each control type round-trip; sensitive retention gate.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SetTextValue"
```

**Commit**

```
feat(M2-02): wpf_set_text_value (L0)

TextBox, PasswordBox, RichTextBox covered. PasswordBox input treated as
SensitiveText by default (S3); opt-out requires AllowSensitiveRetention.
```

---

## M2-03: `wpf_set_check_state` (L0)

**PRD ref:** PRD §5.2 row 12, Applies to {CheckBox, RadioButton}.
**Depends on:** M2-02.
**Estimated:** ~150 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/SetCheckStateStrategy.cs`:
  - Accepts `{ "checked" | "unchecked" | "indeterminate" }`.
  - Rejects `ToggleButton` with `PATTERN_NOT_SUPPORTED` + suggestion
    `wpf_toggle`.
- `SnoopWPF.Agent.Tools/SetCheckStateTool.cs`.
- Tests.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SetCheckState"
```

**Commit**

```
feat(M2-03): wpf_set_check_state (L0)

Deterministic final state (checked/unchecked/indeterminate). Non-CheckBox
non-RadioButton rejected with PATTERN_NOT_SUPPORTED + wpf_toggle suggestion.
```

---

## M2-04a: `wpf_select_item` (L0 basic)

**PRD ref:** PRD §5.2 row 13.
**Depends on:** M2-03.
**Estimated:** ~200 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/SelectItemStrategy.cs` —
  basic (non-virtualized) path:
  - Accepts ItemsControl + identifier (index, text, or inner locator).
  - Sets `IsSelected`/`SelectedItem`.
  - Partial-text match: unambiguous substring only — ambiguous →
    `LOCATOR_AMBIGUOUS`.
- `SnoopWPF.Agent.Tools/SelectItemTool.cs`.
- Tests on non-virtualized `ListBox`, `ComboBox`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SelectItemBasic"
```

**Commit**

```
feat(M2-04a): wpf_select_item (L0 basic — non-virtualized)

ListBox/ComboBox/TreeView/DataGrid non-virtualized path. Virtualized
materialisation lands in M2-04b.
```

---

## M2-04b: `wpf_select_item` (L0 virtualized scroll + partial-text)

**PRD ref:** PRD §5.2 row 13 + §12.3 coverage-gap item.
**Depends on:** M2-04a.
**Estimated:** ~250 LOC; materialiser, scroll loop, tests on 10k-item list.

**Files to edit/create**

- Extend `SelectItemStrategy.cs` with virtualization path:
  - Detects `VirtualizingStackPanel`.
  - Bind/walk `ItemContainerGenerator` for candidates; for off-materialised
    items, scroll via `BringIndexIntoView` / `ScrollIntoView`.
  - Budget: ≤ 20 scroll-materialise iterations before returning
    `ELEMENT_OUTSIDE_VIEWPORT` + suggestion with refined locator.
- Test on `TestWpfApp` `testBigList` fixture (the `VirtualizingStackPanel`-backed
  10,000-item `ListBox` added in M1-06). Since M1-06 lands before M2, the
  fixture is already present; this bead simply drives it.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~SelectItemVirtualized"
```

**Commit**

```
feat(M2-04b): wpf_select_item virtualized materialisation

Scroll-to-materialise loop bounded at 20 iterations. Closes the MC
virtualized-session-list coverage-gap item from PRD §12.3.
```

---

## M2-05: `wpf_click` (L1)

**PRD ref:** PRD §5.2 row 14.
**Depends on:** M1-15 (selector), M2-01 (L0 fallback pattern established).
**Estimated:** ~180 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/ClickStrategy.cs`:
  - `Tier = L1`.
  - `UIElementAutomationPeer.CreatePeerForElement(element).GetPattern(PatternInterface.Invoke) as IInvokeProvider`.
  - `.Invoke()`.
  - If element has `Command` bound, include `suggestion: wpf_execute_command`
    in the response (even on success) as a hint — future v1.1 tightening
    can turn this into a warning.
- `SnoopWPF.Agent.Tools/ClickTool.cs`.
- Tests.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~Click"
```

**Commit**

```
feat(M2-05): wpf_click (L1) fallback

Uses AutomationPeer.Invoke. Prefers L0 — response includes
wpf_execute_command hint when Command DP is bound. L1 cap enforced in
selector.
```

---

## M2-06: `wpf_toggle` (L1)

**PRD ref:** PRD §5.2 row 15. Applies to {ToggleButton, MenuItem IsCheckable}.
**Depends on:** M2-05.
**Estimated:** ~130 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/ToggleStrategy.cs`:
  - `IToggleProvider.Toggle()`.
  - Non-deterministic — PRD §5.2 requires redirect to
    `wpf_set_check_state` for CheckBox/RadioButton.
- `SnoopWPF.Agent.Tools/ToggleTool.cs`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~Toggle"
```

**Commit**

```
feat(M2-06): wpf_toggle (L1)

IToggleProvider.Toggle. CheckBox/RadioButton rejected with redirect to
wpf_set_check_state per PRD §5.2.
```

---

## M2-07: `wpf_expand_collapse` (L1)

**PRD ref:** PRD §5.2 row 16.
**Depends on:** M2-06.
**Estimated:** ~130 LOC.

**Files to create**

- `SnoopWPF.Agent.Input.Deterministic/Strategies/ExpandCollapseStrategy.cs`.
- `SnoopWPF.Agent.Tools/ExpandCollapseTool.cs`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~ExpandCollapse"
```

**Commit**

```
feat(M2-07): wpf_expand_collapse (L1)

IExpandCollapseProvider. TreeViewItem/Expander/GroupItem.
```

---

## M2-08: `wpf_resolve_binding`

**PRD ref:** PRD §5.3, §11 metric #4.
**Depends on:** M1-06 (locator resolution).
**Estimated:** ~300 LOC; most complex single tool in M2.

**Files to create**

- `SnoopWPF.Agent.Engine/Binding/BindingResolver.cs`:
  - Given locator + propertyName, returns full chain:
    - `Path` (e.g. `SelectedSession.User.Name`).
    - `Source` (resolved object).
    - `Intermediate values` at each path step.
    - `Converter` (type name + parameter).
    - `Mode` (OneWay/TwoWay/...).
    - `Validation errors` via `Validation.GetErrors`.
    - `Status` (OK, PathError, ValidationError).
- `SnoopWPF.Agent.Contracts/Dtos/BindingResolutionDto.cs`.
- `SnoopWPF.Agent.Tools/ResolveBindingTool.cs`.
- Tests: clean DP binding, missing DataContext, path-typo, converter-throws.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~ResolveBinding"
```

**Commit**

```
feat(M2-08): wpf_resolve_binding

Full binding chain: path, source, intermediate values, converter, mode,
validation errors. Benchmarked on TestWpfApp bindings; closes PRD §11
metric #4 for the binding-intro use case.
```

---

## M2-09: `wpf_wait_for_property` (flat + presenceExpected)

**PRD ref:** PRD §5.4, §8.1, W3-E2 (`presenceExpected`).
**Depends on:** M1-12 (idle resources).
**Estimated:** ~200 LOC.

**Files to create**

- `SnoopWPF.Agent.Tools/WaitForPropertyTool.cs`:
  - Flat params: `locator, propertyName, expectedValue, timeoutMs?, presenceExpected?`.
  - `presenceExpected: "present" | "absent"` (default present). Supports
    modal-dismissal / negative-existence.
  - Polls via `wpf_poll_changes`-equivalent internal path AND the registered
    idle resources; returns early when condition matches or timeout.
- Tests:
  - Value-equals-X success.
  - `presenceExpected: absent` on a dialog that dismisses → success.
  - Timeout path → `DispatcherBusy` + suggestion `wpf_pump_until_idle`.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~WaitForProperty"
```

**Commit**

```
feat(M2-09): wpf_wait_for_property (flat params + presenceExpected)

Covers ~90% of sync cases agent-side. presenceExpected=absent handles modal
dismissal and negative-existence per W3-E2.
```

---

## M2-10: `wpf_poll_changes`

**PRD ref:** PRD §5.4, §8.1.
**Depends on:** M0-04 (S-3b test already uses a pre-production variant).
**Estimated:** ~150 LOC.

**Files to create**

- `SnoopWPF.Agent.Tools/PollChangesTool.cs`:
  - Params: `sinceVersion, rootLocator?`.
  - Returns changeset dict (nodeIds added / removed / property-mutated) +
    new `treeVersion`.
- Tests including the S-3b scenario in its production form.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~PollChanges"
```

**Commit**

```
feat(M2-10): wpf_poll_changes

Non-blocking polling baseline. Returns immediately with changes since
sinceVersion. Production replacement for the M0-04 harness.
```

---

## M2-11: `wpf_pump_until_idle`

**PRD ref:** PRD §5.4, §8.2 (AND-gate + 5s ceiling).
**Depends on:** M1-12 (idle resources), M2-10.
**Estimated:** ~180 LOC.

**Files to create**

- `SnoopWPF.Agent.Tools/PumpUntilIdleTool.cs`:
  - Params: `timeoutMs? (default 5000), resources?` (array of resource names;
    default all).
  - Blocks until AND-gate is idle OR timeout.
  - On timeout → `AnimationRunawayException` mapped to `DispatcherBusy` +
    failure response.
  - **Nested-pump guard (PRD §8.2)**: if caller thread already holds the
    concurrency semaphore at Send priority, reject with `DISPATCHER_BUSY`.
- Tests including the 5s animation-runaway case.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~PumpUntilIdle"
```

**Commit**

```
feat(M2-11): wpf_pump_until_idle — AND-gate + 5s ceiling

Uses the M1-12 idle-resource registry. Nested-pump guard rejects recursion
with DISPATCHER_BUSY per PRD §8.2 invariant.
```

---

## M2-12: `wpf_fetch_blob`

**PRD ref:** PRD §5.4 utility tool #4.
**Depends on:** none in M2 (can parallel).
**[PARALLEL WITH: M2-08..M2-11]**
**Estimated:** ~120 LOC.

**Context.** Screenshots and large property dumps hand back a `blobRef` the
agent fetches on demand. Keeps tool responses under 64 KB; big payloads
retrieved only when needed.

**Files to create**

- `SnoopWPF.Agent.Engine/Blob/BlobStore.cs` — in-memory `ConcurrentDictionary<string, byte[]>`
  with TTL sweep (5 min default).
- `SnoopWPF.Agent.Tools/FetchBlobTool.cs`.
- Tests.

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~FetchBlob"
```

**Commit**

```
feat(M2-12): wpf_fetch_blob + in-memory BlobStore with TTL

Keeps primary tool responses compact; big payloads (screenshots, property
dumps) retrieved on demand.
```

---

## M2-13: REMOVED — shim moved to MC repo

**Status:** Removed 2026-04-16.

The `IUIActionCatalog` shim is MC-specific and lives in the MC repo
(`/c/work/desktop/wpf-mcp`) as `UiMcpActionCatalog` under
`src/motioncatalyst/Tests/MotionCatalyst.Test.UI/Core/`. Snoopwpf
exposes the tool surface over MCP; MC adapts it to its own
`IUIActionCatalog` interface. Keeping the shim out of snoopwpf avoids
coupling the library to MC's test-interface names.

See `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md` and
`/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` §6.

## M2-14: REMOVED — see M2-13

**Status:** Removed 2026-04-16. Merged with M2-13 under the same
rationale.

---

## M2-15a: VeriGUI harness project + runner + CI integration

**PRD ref:** PRD §11 metric #3, §13 US-MVP-070.
**Depends on:** M2-11.
**Estimated:** ≤400 LOC (C# project + runner + CI YAML).

**Files to create**

- `SnoopWPF.Agent.VeriGuiHarness/SnoopWPF.Agent.VeriGuiHarness.csproj`.
- `SnoopWPF.Agent.VeriGuiHarness/Runner.cs` — reads scenario markdown files
  from `Scenarios/`, drives tool calls, measures:
  - Action success rate.
  - **Repeat-on-unchanged-state rate** (acceptance: < 5% per §11.3).
  - p95 per-call latency (acceptance: < 10 ms per §11).
- CI workflow step: runs runner, asserts thresholds non-zero, uploads report.

**Acceptance criteria**

```bash
dotnet.exe run --project /c/work/snoopwpf/SnoopWPF.Agent.VeriGuiHarness -- \
    --run --out /tmp/verigui-report.json --assert-pass-thresholds
# Runner exits non-zero if repeatOnUnchangedRate >= 0.05 or p95PerCallMs >= 10.
# No jq required — thresholds are evaluated inside the runner.
```

**Commit**

```
test(M2-15a): VeriGUI harness runner + CI integration

Runner reads Scenarios/*.md, measures repeat-on-unchanged-state rate and p95
latency; exits non-zero on threshold violation (no jq required). Scenarios
land in M2-15b.
```

---

## M2-15b: VeriGUI 100-scenario authoring

**PRD ref:** PRD §11 metric #3, §13 US-MVP-070.
**Depends on:** M2-15a.
**Estimated:** ≤800 LOC of scenario markdown; runner unchanged from M2-15a.

**Files to create**

- `SnoopWPF.Agent.VeriGuiHarness/Scenarios/*.md` — 100 scenarios following
  the VeriGUI template (action + expected state delta). Auto-seed N scenarios
  by replaying integration tests with a random-intent injector; hand-curate
  the remaining (100 − N). Minimum N is whatever the integration test suite
  produces organically — capture replays, then author the remainder by hand.

**Acceptance criteria**

```bash
# Scenario count
ls /c/work/snoopwpf/SnoopWPF.Agent.VeriGuiHarness/Scenarios/*.md | wc -l  # expect ≥ 100
# Full run passes
dotnet.exe run --project /c/work/snoopwpf/SnoopWPF.Agent.VeriGuiHarness -- \
    --run --out /tmp/verigui-report.json --assert-pass-thresholds
```

**Commit**

```
test(M2-15b): VeriGUI 100 scenarios (N auto-seeded + (100-N) hand-curated)

Measures repeat-on-unchanged-state rate; §11 metric #3 target < 5%. Closes
the VeriGUI acceptance gate.
```

---

## M2-16: Coverage-gap closure per PR-1 outcome

**PRD ref:** PRD §12.3.
**Depends on:** M0-06 (audit). [M2-14 REMOVED per 2026-04-16 arch change.]
**Estimated:** variable (0–800 LOC), depending on audit outcome.

**Context.** The audit (M0-06) decides per scenario: shim covers / FlaUI stays /
descoped. This bead implements only the "shim covers" additions identified.
There is no Shim.FlaUI project in this repo (that moved to the MC repo as
`UiMcpActionCatalog`). Coverage features are added as new strategies under
`SnoopWPF.Agent.Input.Deterministic/Strategies/` or as broker-scaffolding
features under M2-21 where appropriate.

**Steps**

1. Read `COVERAGE-GAP-AUDIT.md`.
2. For each scenario marked "shim covers" in the audit but not yet supported,
   add the needed strategy/tool extension. Common cases:
   - Slider value setter with range normalization → new strategy under
     `SnoopWPF.Agent.Input.Deterministic/Strategies/`.
   - License-dialog detection → use `wpf_wait_for_property(presenceExpected)`.
   - `ResetToHome` state-machine reset → broker lifecycle tool in M2-21
     (MC-specific tools live in the MC repo; generic scaffolding lives in BrokerHost).
3. Add one test per covered scenario.
4. Any scenario marked "FlaUI stays" → document in
   `docs/shim-retained-flaui.md`.

**Acceptance criteria**

```bash
# Every coverage-gap scenario has a matching integration test
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~CoverageGap"
```

**Commit**

```
feat(M2-16): coverage-gap closure — <N> scenarios

Scope driven by M0-06 audit. Adds <list of strategies/helpers> under
Input.Deterministic/Strategies/. FlaUI-retained scenarios documented in
docs/shim-retained-flaui.md. [M2-14 REMOVED per 2026-04-16 arch change.]
```

---

## M2-17: CI dual-stack finalize

**PRD ref:** PRD §12.4, §13 US-MVP-071.
**Depends on:** M0-07 (PR-2 baseline CI). [M2-14 REMOVED; flaui-suite job
scope replaced by brokered-suite per 2026-04-16 arch change.]
**Estimated:** ~100 LOC YAML edits.

**Files to edit**

- `.github/workflows/agent-ci.yml` — add `brokered-suite` job alongside
  `snoop-suite` job. The `brokered-suite` runs the Brokered-mode integration
  tests output by M2-21 (broker + target round-trip, reconnect, redaction
  target-side, negative handshake tests). Matrix net8 on x64 `windows-latest`.
  Both `snoop-suite` and `brokered-suite` must pass.

**Acceptance criteria** (MANUAL VERIFICATION)

Push to a feature branch, open a PR, confirm both `snoop-suite` and
`brokered-suite` jobs go green. Report the PR URL in the commit body.

**Commit**

```
ci(M2-17): dual-stack CI — brokered-suite + snoop-suite (both-pass gate)

Adds brokered-suite job running M2-21 integration tests. Closes PRD §12.4 /
US-MVP-071. [M2-14 flaui-suite removed per 2026-04-16 arch change.]
PR URL: <insert URL>
```

---

## M2-18: NuGet packaging + signing + feed decision

**PRD ref:** PRD §13 US-MVP-072.
**Depends on:** M2-01..M2-17.
**Estimated:** ~150 LOC across .csproj + `nuspec` + release workflow.

**Files to edit/create**

- `SnoopWPF.Agent.Server/SnoopWPF.Agent.Server.csproj` — packable metadata
  (PackageId=SnoopWPF.Agent, icon, readme, license, repo URL, tags).
- `SnoopWPF.Agent.BrokerHost/SnoopWPF.Agent.BrokerHost.csproj` — packable
  metadata. This is the new ClassLibrary project created in M2-21 (type
  ClassLibrary, `net8.0-windows`), consumed by external brokers (including
  MC's `UiMcpHost`). Note: the existing `SnoopWPF.Agent.Host` (the injection-mode
  `snoop-mcp.exe`, OutputType=Exe) stays unchanged and is NOT packaged as a
  library NuGet.
- `SnoopWPF.Agent.Remote/SnoopWPF.Agent.Remote.csproj` — packable metadata.
  Pipe client + framing; consumed by external brokers.
- `SnoopWPF.Agent.Contracts/SnoopWPF.Agent.Contracts.csproj` — packable
  metadata. DTOs + `WpfLocator` + interfaces.
- `SnoopWPF.Agent.Analyzers/SnoopWPF.Agent.Analyzers.csproj` — analyzer
  packaging convention (content `analyzers/dotnet/cs/`).
- `.github/workflows/release.yml` — on tag `v*`, build in Release, sign
  (SignPath.io free cert already used by upstream), publish to feed.
- **Feed decision**: GitHub Packages initially; nuget.org after first
  stable release per PRD. Document in `docs/packaging.md`.

**Acceptance criteria**

```bash
dotnet.exe pack /c/work/snoopwpf/SnoopWPF.Agent.Server/SnoopWPF.Agent.Server.csproj -c Release
ls /c/work/snoopwpf/SnoopWPF.Agent.Server/bin/Release/SnoopWPF.Agent.*.nupkg | head -1
test -f /c/work/snoopwpf/docs/packaging.md
```

**Commit**

```
build(M2-18): NuGet packaging + release workflow

SnoopWPF.Agent.Server + .Host + .Remote + .Contracts + .Analyzers
packable; SignPath.io signing; initial feed GitHub Packages with nuget.org
flip documented.
```

---

## M2-21: `SnoopWPF.Agent.BrokerHost` broker scaffolding (new library)

> **NEW 2026-04-16** per architecture change. Read
> `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md` first.

**PRD ref:** PRD §4.1 Brokered mode. Generic broker pieces only —
lifecycle tools like `mc_launch` are MC-specific and live in the MC
repo's `UiMcpHost` project, not here.
**Depends on:** M1-21 (`StartBrokered`), M1-22 (pipe framing).
**Estimated:** ~500 LOC + tests.

**Scope**

Create a **new project** `SnoopWPF.Agent.BrokerHost/` (type ClassLibrary,
`net8.0-windows`). The existing `SnoopWPF.Agent.Host` is the injection-mode
`snoop-mcp.exe` (OutputType=Exe, `AssemblyName=snoop-mcp`) and stays
completely unchanged. Do NOT modify it.

`SnoopWPF.Agent.BrokerHost` exposes:

- `BrokerHost.Start(McpServerTransport transport, BrokerOptions opts)` —
  sets `Console.SetOut(TextWriter.Null)` as its first statement (broker owns
  MCP stdio), then installs the 18-tool MCP surface; each tool routes through
  a pipe client to whichever target is currently connected.
- `BrokerOptions.PipeName` — name of the pipe to connect to.
- `BrokerOptions.OnTargetDisconnected` — callback hook for external lifecycle
  code (MC's `UiMcpHost` uses this to surface `TARGET_NOT_RUNNING` failures).
- Generic tool-proxy registration that downstream consumers extend with their
  own lifecycle tools (`mc_launch`, etc).
- `BrokerTargetSpawner.Spawn(exe, args, pipeName, tokenHex)` — calls
  `Process.Start` with `UseShellExecute=false`, `CreateNoWindow=true`,
  `RedirectStandardOutput=true`, `RedirectStandardError=true`. Spawns
  background drain-tasks that read and discard target stdout/stderr (optionally
  forward to a log file, never to broker's stdout).
- No MC-specific code in this project — lifecycle tools belong to the external
  broker consuming this library.

**Files to create** (all under `SnoopWPF.Agent.BrokerHost/`)

- `SnoopWPF.Agent.BrokerHost/SnoopWPF.Agent.BrokerHost.csproj` — ClassLibrary,
  `net8.0-windows`, references `SnoopWPF.Agent.Contracts`, `SnoopWPF.Agent.Remote`.
- `SnoopWPF.Agent.BrokerHost/BrokerHost.cs`
- `SnoopWPF.Agent.BrokerHost/BrokerOptions.cs`
- `SnoopWPF.Agent.BrokerHost/ToolProxyRegistrar.cs`
- `SnoopWPF.Agent.BrokerHost/BrokerTargetSpawner.cs`

**Tests**

- Unit test: `BrokerTargetSpawner.Spawn` produces a `Process` with
  `StartInfo.RedirectStandardOutput == true`.
- Integration test: broker launches sample target with
  `--snoop-pipe=<name> --snoop-token=<hex>`; target's stdout does NOT appear
  on broker's stdout (assert by reading broker's stdio for 500 ms, confirm
  empty).
- Integration test: broker + target round-trip over all 18 tools; each tool
  call succeeds (this feeds M2-19).
- Unit test: asserts `BrokerHost` does NOT instantiate `AuditLogWriter`
  (target-only audit invariant from M1-13 / B-5).

**Acceptance criteria**

```bash
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests \
    --filter "FullyQualifiedName~BrokerHost"
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~BrokerHost|FullyQualifiedName~BrokerTargetSpawner"
```

**Commit**

```
feat(M2-21): SnoopWPF.Agent.BrokerHost — new library (not converted from Host EXE)

New ClassLibrary net8.0-windows. Console.SetOut(TextWriter.Null) as first
statement (broker owns MCP stdio). BrokerTargetSpawner redirects target
stdout/stderr so child output never reaches broker stdio. Audit is
target-only (BrokerHost never constructs AuditLogWriter). MC's UiMcpHost
adds lifecycle tools on top.
```

---

## M2-19: Brokered-mode consumer deliverables (snoopwpf-side only)

**PRD ref:** PRD §12, §4.1 Brokered mode. Cross-repo work —
consumer-side PRD at `/c/work/desktop/wpf-mcp/PRD-snoop-integration.md`
governs MC-side changes (including `UiMcpHost.exe`). This bead tracks
only the snoopwpf-side deliverables that MC depends on.
**Depends on:** M2-18 (pre-release packages on GitHub Packages feed),
M2-21 (`SnoopWPF.Agent.Host` broker scaffolding).
**Estimated:** ~2 weeks.

**Scope (this repo only)**

- **Edit** `Samples/SnoopWPF.SampleApp/Program.cs` — add `--mcp-stdio`
  and `--snoop-pipe=<name>` flag parsing. Also add a `--smoke` self-test
  flag that calls `wpf_get_session_info`, asserts `windows.Count >= 1`, and
  exits 0 on success (non-zero on failure). This enables the M2-19 acceptance
  criteria to be validated with a single runnable command.
- Sample app (`Samples/SnoopWPF.SampleApp`) gains two launch paths:
  - `--mcp-stdio` — existing co-located demo (unchanged).
  - `--snoop-pipe=<name>` — new brokered demo. Sample runs as the
    target, spawned by a test broker.
- Sample broker (`Samples/SnoopWPF.SampleBroker`) — a minimal
  external broker demonstrating the `SnoopWPF.Agent.Host` +
  `.Remote` package consumption. Includes lifecycle tools
  (`sample_launch`, `sample_exit`) mirroring the shape MC's
  `UiMcpHost` will implement.
- Docs `docs/brokered-mode-integration.md` with the exact target-side
  `Program.cs` patch (`--snoop-pipe` flag, `StartBrokered`
  dispatcher call) and broker-side skeleton as reference for
  consumers.
- Brokered-mode integration test in `SnoopWPF.Agent.IntegrationTests`:
  sample broker spawns sample target, issues each tool in the 18-tool
  surface over the pipe, asserts state-delta schema across all
  mutation tools.

**Acceptance criteria**

```bash
# Co-located sample still works (back-compat check)
dotnet.exe run --project /c/work/snoopwpf/Samples/SnoopWPF.SampleApp -- --mcp-stdio --smoke
# Brokered sample: broker + target round-trip
dotnet.exe run --project /c/work/snoopwpf/Samples/SnoopWPF.SampleBroker -- --smoke
# Integration test green
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests \
    --filter "FullyQualifiedName~BrokeredRoundTrip"
test -f /c/work/snoopwpf/docs/brokered-mode-integration.md
```

**M2 GATE** — closes v5-MVP. All of:

```bash
dotnet.exe build /c/work/snoopwpf/Snoop.sln -c Release -p:TreatWarningsAsErrors=true
dotnet.exe test /c/work/snoopwpf/Snoop.Core.Tests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Tests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.IntegrationTests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.InjectionTests
dotnet.exe test /c/work/snoopwpf/SnoopWPF.Agent.Analyzers.Tests
dotnet.exe run --project /c/work/snoopwpf/SnoopWPF.Agent.VeriGuiHarness -- \
    --run --out /tmp/verigui-report.json --assert-pass-thresholds
# Runner exits non-zero if repeatOnUnchangedRate >= 0.05 or p95PerCallMs >= 10.
```

**Commit**

```
feat(M2-19): brokered-mode consumer deliverables

Sample target + sample broker + integration test + docs. MC-side
UiMcpHost.exe consumes the Host + Remote NuGets published in M2-18.
Closes v5-MVP per PRD §11 gate metrics.
```

---

# PRD↔Beads Divergence Log

This log records where this file deviates from `PRD-v5-MVP.md`. Every
divergence is either a simplification (bead is smaller scope than PRD
claimed) or a clarification (bead adds detail PRD deferred to "BEADS-MVP").
If a divergence conflicts with PRD intent, flag it for a PRD amendment.

| Bead   | PRD ref   | Divergence | Rationale |
|--------|-----------|-----------|-----------|
| MP-02  | §10 M-pre | SnoopLog ACL **removed** from docs rather than implemented | PRD offered (a) implement or (b) delete-claim; (b) chosen for MVP because ACL failure under injection-launcher exception paths must not crash the launcher. Proper fix is v1.1 with its own bead (MP-02 records this as in-code TODO). |
| MP-03  | §10 M-pre | Fails forward >4 drift items (doesn't fix inline) | Keeps the bead atomic. Large drift sweeps are their own bead phase, not buried inside a sweep. |
| M0-03  | §10 M0 S-3 | Benchmark adds `BenchmarkDotNet` as a new dep | PRD didn't specify the benchmarking harness; BDN is industry-standard and already in transitive graph. |
| M1-04  | §9.7 MF-11 | Adds runtime assertion in addition to factory override | Defense-in-depth against future refactors that bypass the factory. Zero cost, catches regressions loudly. |
| M1-06  | §14 debt item | Fixes `ParentNodeId` hardcoded-empty as part of the locator bead | PRD said "close as part of WpfLocator bead's acceptance criteria"; done here. |
| M1-15  | §4.4 | `InputIntent` enum lives in Contracts, not in Input.Deterministic | Contracts is shared by Injection/Remote which need to serialize the kind; placement matches the `[DataContract]` pattern elsewhere. |
| M1-19  | §9.5 | Adds an integration test asserting no `Console.Write*` leaks | PRD cites "fixed inline" for bugs 1–2; this bead adds regression guard so re-fixing is caught. |
| M2-04  | §5.2 row 13 | Split into M2-04a (basic) + M2-04b (virtualized) | Single bead > 500 LOC; global rule splits at 500 LOC. Two atomic commits. |
| M2-15  | §11 metric #3 + §13 US-MVP-070 | Harness is a new project, not test-suite code | 100 scenarios + runner + report is too much for a test fixture; standalone project clarifies ownership and CI path. |
| M2-18  | §13 US-MVP-072 | Defers `nuget.org` publication to post-first-stable | PRD allowed feed choice; GitHub Packages first lets us iterate on package shape without nuget.org irreversibility. |
| M2-19  | §12 | This repo's half; MC repo has its own beads in consumer-side PRD | PRD §15 treats M2+MC as serial; this ID captures only the snoopwpf-side deliverables. |
| M1-21  | §4.1, §4.2 (post 2026-04-16 arch change) | NEW bead — not in original PRD | `StartBrokered` API added after 2026-04-16 architecture change; see `ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md`. |
| M1-22  | §4.1, §4.2 (post 2026-04-16 arch change) | NEW bead — not in original PRD | Brokered-mode pipe framing; possibly reuses existing `SnoopWPF.Agent.Remote` pieces. |
| M2-21  | §4.1 (post 2026-04-16 arch change) | NEW bead — not in original PRD | Generic broker scaffolding in `SnoopWPF.Agent.Host`; MC's `UiMcpHost` consumes from NuGet. Lifecycle tools are MC-specific and live in the MC repo, not here. |
| M2-13  | §12.3 | REMOVED post 2026-04-16 | Shim moved to MC repo as `UiMcpActionCatalog` to avoid coupling snoopwpf to MC's test interfaces. |
| M2-14  | §12.3 | REMOVED post 2026-04-16 | Same rationale as M2-13. |
| FD-2 (M1-01) | §4.3 (post 2026-04-16) | `SessionMode` enum widened from 2 values `{CoLocated=0, Injection=1}` to 3 values `{CoLocated=0, Brokered=1, Injection=2}` | Architecture change adds Brokered as third integration mode; FD-2 frozen block updated to match. |
| FD-3 (M1-10) | §7.4 (post 2026-04-16) | `FailureReason` enum widened from 12 to 13 values; `TargetNotRunning = 12` added | Brokered mode needs a machine-executable failure code when no target is connected; maps to `broker_launch_target` suggestion. |
| M1-21 | §4.2 §9.7 (post 2026-04-16 hardening review) | `StartBrokered` gains explicit `sessionTokenHex` parameter; `PipeOptions.CurrentUserOnly` required; reconnect loop required | 4-reviewer security round mandated pipe hardening symmetric with v3 injection-mode pipe; reconnect loop required for broker crash/restart without target restart. |
| M2-15 | §11 metric #3 §13 US-MVP-070 | Split into M2-15a (harness + runner, ≤400 LOC) and M2-15b (100 scenarios, ≤800 LOC) | Single bead was >1200 LOC; global rule splits at 500 LOC. Runner and scenarios have independent ownership and review cycles. |
| M2-21 | §4.1 (post 2026-04-16 arch change) | Creates new `SnoopWPF.Agent.BrokerHost` ClassLibrary; does NOT convert or modify existing `SnoopWPF.Agent.Host` injection EXE | The existing Host EXE (`snoop-mcp.exe`, OutputType=Exe) must remain unchanged as the injection-mode host. A separate ClassLibrary is required for NuGet consumption by external brokers. |
| Footer count | Cross-file | Corrected from 49 to 51 atomic beads: M1 gains M1-21 and M1-22 (+2); M2-15 split into M2-15a and M2-15b (+1); M2-21 added (+1); M2-13 and M2-14 kept as REMOVED stubs (counted) | Total: M-pre(3) + M0(7) + M1(22) + M2(19) = 51. |

Entries not listed here indicate the bead matches the PRD exactly.

---

# Open Questions (resolve before M1 starts)

1. **Analyzer project placement.** M1-16/17/18 all live in
   `SnoopWPF.Agent.Analyzers`. NuGet-package the analyzer separately or bundle
   inside `SnoopWPF.Agent` (server package)? Bundling is simpler for MVP;
   separate package aligns with conventions. Decision owner: NuGet bead
   (M2-18).
2. **`wpf_set_text_value` vs `PasswordBox.Password` (non-DP).** Strategy
   uses `SetValue` at DP level; `PasswordBox.PasswordProperty` is a
   `PropertyPath`-only DP (not a public CLR property). Confirm at
   implementation time that the strategy can write via PropertyPath. If not,
   the strategy falls back to setting the public `Password` property via
   reflection (still inside `SetTextValueStrategy`, still gated by the
   selector).
3. **Shim.FlaUI test suite — where does it live?** Create
   `SnoopWPF.Agent.Shim.FlaUI.Tests` (new project) or fold into
   `SnoopWPF.Agent.IntegrationTests`? Closer to production code (new project)
   is preferred for CI separation per M2-17.
4. **VeriGUI scenario authoring.** 100 scenarios is a lot of manual authoring.
   Can we auto-seed N scenarios by replaying integration tests with a
   random-intent injector, then hand-curate the remaining (100 - N)? Decide
   during M2-15 design step.

Surface unresolved items to the PRD for amendment; do not mutate the PRD
from a bead.

---

*End of BEADS-MVP. 51 atomic beads across M-pre (3) + M0 (7) + M1 (22) +
M2 (19). Execute in order; parallel markers explicit. Every bead ends in one
atomic commit. Gates at M1 close and M2 close reference runnable commands.
When M2-19 commits green, v5-MVP ships.*
