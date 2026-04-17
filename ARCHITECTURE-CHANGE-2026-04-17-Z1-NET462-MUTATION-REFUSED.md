# Architecture Decision: FX6-Z1 — EnableMutation=true refused on net462 injection targets

**Date:** 2026-04-17
**Bead:** bd-1we.10 (FX6-Z1)
**Status:** Implemented

## Problem

`AuditLogWriter` depends on `System.Threading.Channels`, which is a net6+ API.
When the agent DLL is injected into a .NET Framework 4.6.2 WPF process, the net462
build of `SnoopWPF.Agent.Engine` is loaded. `System.Threading.Channels` is not
available on net462, so the audit log is silently dropped.

Security impact: a mutation session against a .NET Framework 4.6.2 target produces
no audit record, violating the agent's security contract.

## Options Considered

| Option | Description | Cost |
|--------|-------------|------|
| A | Backport a minimal bounded-queue audit writer for net462 using `BlockingCollection` | High — `BlockingCollection` has different back-pressure semantics; separate test coverage required |
| B | Forbid `EnableMutation=true` on net462 targets at session start | Low — one `#if` guard + error code |
| C | Pipe audit events back to the broker process (net6+) via the existing pipe protocol | High — requires protocol extension + broker-side writer |

## Decision: Option B

Option B was selected as the cheapest option that eliminates the security gap without
introducing new complexity or backport maintenance burden.

**Rationale:**
- The injection audit trail gap is a security issue, not a feature gap. The correct
  response to "we cannot audit this" is to refuse the operation, not to silently proceed.
- net462 targets are legacy. Requiring the target application to run on .NET 6+ in order
  to use mutation features is a reasonable constraint that aligns with the broader
  .NET lifecycle (net462 reached end-of-mainstream-support in 2022).
- Option A would require maintaining a parallel `BlockingCollection`-based queue writer
  with different threading semantics than the `Channel`-based one, increasing test surface
  and maintenance burden for a declining-use target framework.
- Option C would require a protocol extension (audit frames over the pipe) and a
  broker-side writer, significantly increasing complexity.

## Implementation

In `SnoopWPF.Agent.Injection/SnoopAgentEntryPoint.cs`, `StartCore()`:

```csharp
#if !NET6_0_OR_GREATER
if (injectionAgentOptions.EnableMutation)
{
    throw new SnoopException(
        SnoopErrorCode.UnsupportedOnNet462,
        "EnableMutation=true is not supported when the agent is injected into a .NET Framework " +
        "4.6.2 target. The audit log subsystem requires System.Threading.Channels (net6+). " +
        "A mutation session with no audit trail is not permitted. " +
        "Use a .NET 6 or later target application to enable mutation.");
}
#endif
```

`SnoopErrorCode.UnsupportedOnNet462` was added in the D2 bead (bd-1we.4.2).

## Consequence

- Mutation calls against net462 injection targets fail at session start with a typed,
  actionable error message.
- Read-only inspection continues to work on net462 targets without restriction.
- The current injection entry point hardcodes `EnableMutation = false`, so the guard
  does not fire today; it is a forward-compatibility barrier that prevents a future
  code change from accidentally enabling mutation on net462 without addressing the
  audit gap.
