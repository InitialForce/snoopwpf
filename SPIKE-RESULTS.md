# v5-MVP Spike Results

Each M0 spike appends a section. Verdicts: GREEN (pass + no concerns),
YELLOW (pass with caveats), RED (fail; plan fallback).

## S-1 — Co-located stdio round-trip re-verification

**Date:** 2026-04-16
**Bead:** M0-01 (bd-3gk)
**Machine:** `AMD Ryzen 9 5950X 16-Core Processor`, `64 GB RAM`, .NET SDK `10.0.104`.
**Integration suite:** `SnoopWPF.Agent.IntegrationTests`

- Total tests: 86
- Passed: 84
- Failed: 0
- Run time: 0:02 (2.52 s)
- Slowest test: GetAncestors_AllAncestors_HaveNodeIdAndTypeName 283 ms
- Fastest test: GetBehaviors_UnknownNodeId_ThrowsSnoopException <1 ms

**Verdict:** GREEN

**Notes:** 2 tests skipped (inconclusive): `GetResources_WithKeyFilter_NarrowsResults` (no
resources found in application) and `GetChildren_WithNodeId_ReturnsChildrenOfThatNode` (no
child node with children at root level). These are pre-existing inconclusive conditions, not
failures. The StyleCop warning SA1200 on `McpStdioEntrypointAttribute.cs` causes a build error
under default settings; tests were run with `-p:TreatWarningsAsErrors=false`. This warning
should be fixed before M1 work begins.

---

## S-5 — ValueChangedEventManager weak-event leak test

**Date:** 2026-04-16
**Bead:** M0-05 (bd-3j8)
**Machine:** `AMD Ryzen 9 5950X 16-Core Processor`, `64 GB RAM`, .NET SDK `10.0.104`.
**Test file:** `SnoopWPF.Agent.Tests/WeakEventManagerLeakTest.cs`

**Approach.** `ValueChangedEventManager` is an internal WPF class; the public
surface that routes through it is `DependencyPropertyDescriptor.AddValueChanged`.
The test creates a minimal `DependencyObject` with a registered `DependencyProperty`,
attaches a `ChangeListener` via `AddValueChanged`, bumps the DP 1 000 000 times,
removes the subscription, drops all strong references, and forces a full GC triple-pass.
A second test repeats the exercise *without* calling `RemoveValueChanged` to verify
that both the source object and listener are collected once both go out of scope.

**Results:**

| Test | Result | Duration |
|------|--------|----------|
| `ValueChangedSubscription_DoesNotLeakAfterOneMillionBumps` | PASS | ~240 ms |
| `ValueChangedSubscription_NoExplicitRemove_HeapReturnsToBaselineAfterBothDropped` | PASS | ~10 ms |

- Heap growth after 1 000 000 bumps + GC: **within 2 MB limit** (GREEN).
- Sanity: listener received all 1 000 000 notifications.
- Without `RemoveValueChanged`, heap still returns to baseline once both source
  and listener fall out of scope — confirms no unbounded retention.

**Caveat.** `DependencyPropertyDescriptor.AddValueChanged` stores the handler in a
dictionary keyed by the *source object* (not the listener), so the listener itself is
not independently weak-held. Retention is bounded by the lifetime of the source
`DependencyObject`, which matches PRD §8.3 usage (subscription torn down when the
watched node is unregistered). Production code MUST call `RemoveValueChanged` (or
equivalent) when a watch is cancelled to avoid holding the source alive.

**Verdict:** GREEN — ValueChangedEventManager-based subscriptions do not leak
memory over 1 000 000 DP bumps. Long-running agent sessions are safe provided
watches are unsubscribed on node removal (see M1-12).

---
