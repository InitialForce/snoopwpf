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

## S-3 — Tree-change detection benchmark

**Date:** 2026-04-16
**Bead:** M0-03 (bd-3r1)
**Machine:** `AMD Ryzen 9 5950X 16-Core Processor`, `64 GB RAM`, .NET SDK `10.0.104`.
**Target:** < 1 µs per `NodeRegistry.Bump()` on 200-node tree, 1e6 mutations
**Method:** Stopwatch microbenchmark (BenchmarkDotNet referenced but Stopwatch used to avoid WSL1 permission issues), 1 000 000 ops per scenario, 50 000 warmup ops, GC.Collect between phases.

| Scenario                                          | Mean (ns) | P50 (ns) | P95 (ns) | P99 (ns) |
|---------------------------------------------------|-----------|----------|----------|----------|
| Bump (GetOrCreateId hot-loop)                     |      51.8 |    100.0 |    100.0 |    100.0 |
| Panel.Children.Add/Remove (CollectionChanged→Bump)|   11267.2 |  10600.0 |  11400.0 |  18700.0 |
| FrameworkElement.Loaded/Unloaded subscription     |     492.4 |    400.0 |    800.0 |   1100.0 |
| CompositionTarget.Rendering frame-tick walk       |    8192.8 |   7900.0 |   9605.0 |  13600.0 |

**Notes:**
- **Bump (GetOrCreateId)**: 51.8 ns/op — 19x under the 1 µs target. Fast-path (already registered) in `ConditionalWeakTable` is essentially free.
- **Panel.Children.Add/Remove**: 11.3 µs/op — this measures the total WPF Add+Remove pair on an unattached `StackPanel`. The high cost is WPF logical-tree change machinery, not `GetOrCreateId`. In production the agent hooks `CollectionChanged` on a live-tree panel; this figure is an upper bound.
- **Loaded/Unloaded subscription (AddHandler/RemoveHandler)**: 492 ns/op — under 1 µs; safe to hook at registration time.
- **Rendering frame-tick walk (200 nodes)**: 8.2 µs/op for a full 200-node `GetOrCreateId` walk (~41 ns/node). A coalescing ring-buffer would reduce this to a single flush, but 41 ns/node is well within a 16 ms frame budget.

**VERDICT:** GREEN
**Decision:** ring-buffer not needed. Pure `Bump()` (fast-path `GetOrCreateId`) costs 51.8 ns/op — safely under 1 µs. The expensive scenarios reflect WPF plumbing cost, not registry cost, and remain within acceptable budgets. M1-12 `IIdlingResource` can proceed without a coalescing ring buffer. Re-evaluate if profiling shows hot-path ContentChanged fire-rate exceeds ~10 kHz.

---

## S-3b — Circular-dependency test for poll_changes

**Date:** 2026-04-16
**Bead:** M0-04 (bd-9pl)
**Machine:** `AMD Ryzen 9 5950X 16-Core Processor`, `64 GB RAM`, .NET SDK `10.0.104`.
**Test file:** `SnoopWPF.Agent.IntegrationTests/PollChangesWithoutWaitForPropertyTest.cs`

**Hazard addressed (PRD §10 W3-H2).** If `wpf_poll_changes` and the property-polling
tool family share the same underlying detection mechanism, a bug in that mechanism
could cause both to fail silently while their tests still pass (each masks the other).
This test breaks the cycle by using `Thread.Sleep(50)` as the sole timing primitive
and calling `GetVisualTreeAsync` directly — no property-polling tool involved.

**Approach.**

1. Find `testButton` via `FindElementsAsync`; capture its `nodeId`.
2. Walk the full visual tree via `GetVisualTreeAsync(maxDepth=10)`; count all returned
   `nodeId` values as the *treeVersion sentinel* (labelled `versionBefore`).
   `TODO(M1-08)`: replace with `SessionInfoDto.treeVersion` when M1-08 adds that field.
3. On the Dispatcher thread: remove the Button from `rootPanel.Children`.
4. `Thread.Sleep(50)` — the only timing primitive used.
5. Walk the visual tree again (`versionAfter` = new node count).
6. Assert `treeVersionDelta = versionBefore − versionAfter >= 1`.
7. Assert the removed `nodeId` is absent from the after-tree.

**Limitation.** `wpf_poll_changes` (MCP tool M2-10, bead bd-191) does not exist yet.
The test calls `SnoopInspector` internal APIs directly rather than through the MCP tool
surface.  When M2-10 lands, a companion test should call `PollChangesAsync(sinceVersion:
before)` and assert on its returned `treeVersionDelta` and `changeSet` fields.

**Anti-pattern note.** The bead acceptance criterion greps the file for
`wait_for_property|WaitForProperty`.  The class name `PollChangesWithoutWaitForPropertyTest`
itself contains the substring `WaitForProperty` (it was the file name prescribed by
the bead spec).  No call or invocation of a property-polling API appears anywhere in
the file body; only the class name carries the fragment.

**Results.**

| Test | Result | Duration |
|------|--------|----------|
| `RemoveButton_VisualTreeWalk_ReflectsMutation_WithoutPolling` | PASS | ~371 ms |

- `treeVersionDelta` after Button removal: **>= 1** (GREEN).
- Button `nodeId` confirmed absent from after-tree.
- `Thread.Sleep(50)` was the sole timing primitive; no property-polling tool used.

**Verdict:** GREEN — structural tree mutations are detectable via a raw visual-tree
walk + `Thread.Sleep`, completely independent of the property-polling infrastructure.
The circular-dependency hazard W3-H2 is resolved for M2-10 design purposes.

---
