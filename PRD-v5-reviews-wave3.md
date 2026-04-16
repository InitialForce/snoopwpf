# PRD v5-MVP — Wave 3 Review Synthesis

Four fresh-eyes Opus reviewers, each briefed with a distinct lens and
explicitly forbidden from re-deriving Wave-2 findings.

- **R-A — Red Team / Failure-Mode Hunter**: contract ambiguities,
  concurrency hazards, adversarial inputs, 10th security hole.
- **R-B — Agent-Consumer UX**: simulates an LLM calling the 20 tools;
  naming disambiguation, error-message coherence, state-delta token cost.
- **R-C — YAGNI Hardliner**: argues to cut to ~10 tools, 6–8 weeks, with
  spot-checks against real MotionCatalyst ViewModels.
- **R-D — Delivery Realism**: critical-path audit, hidden work, honest
  P50/P90 against the PRD's 11–17 week claim.

All four reviewers were briefed on the Wave-2 findings and instructed NOT
to re-derive them. This document is everything new.

---

## Convergent critical findings (flagged by 2+ reviewers)

### W3-C1 — PRD underspecifies the state-delta contract in load-bearing ways

- **R-A #3**: `stateChanged` computation timing is undefined. A
  re-entrant `PropertyChangedCallback` (DependencyProperty metadata) can
  synchronously revert a value on the same Dispatcher frame after
  `SetValue` returns. `stateChanged` would be `true` (Bump fired) but
  `currentValue == previousValue`. Agent sees "success, state changed"
  and retries — infinite escalation loop.
- **R-B #2**: `failureReason` is described as "a structured error code"
  with no enum; `suggestion` is described as "machine-executable" with
  no schema. Two implementers will ship incompatible shapes.
- **R-B #3**: `treeVersionBefore`, `treeVersionAfter`, `elapsedMs`,
  `chosenTier`, `actionabilityChecksPassed` on success are dead weight
  per-call token cost — none are agent-actionable.
- **Resolution**: Rewrite §7 canonical state delta with:
  1. Explicit rule: `stateChanged` is computed at response-serialization
     time against `previousValue` captured pre-call, not at `SetValue`
     time. Document re-entrant callback behaviour.
  2. Ship the 12-value `failureReason` enum from R-B §2 verbatim
     (`ELEMENT_NOT_FOUND`, `ELEMENT_NOT_VISIBLE`, `ELEMENT_NOT_ENABLED`,
     `CANNOT_EXECUTE_COMMAND`, `AUTOMATION_DISABLED`, `MUTATION_DISABLED`,
     `TIER_MISMATCH`, `STATE_UNCHANGED`, `LOCATOR_AMBIGUOUS`,
     `DISPATCHER_BUSY`, `ELEMENT_OUTSIDE_VIEWPORT`,
     `PATTERN_NOT_SUPPORTED`).
  3. `suggestion` schema is `{ tool: string, args: object } | null`, not
     a prose string. Agent parses and re-invokes directly.
  4. Replace `treeVersionBefore`/`treeVersionAfter` with just
     `treeVersionDelta`. Drop `elapsedMs`, `chosenTier` from default
     response; re-enable behind a `debug: true` call hint. Rename
     `actionabilityChecksPassed` → `actionabilityChecksFailed`, populate
     only on failure.

### W3-C2 — 3-call success metric (§11 #1) is unachievable as written

- **R-B #5**: `wpf_get_session_info` returns session mode/tier/policy but
  not the list of top-level windows. Bootstrapping to "click Save" takes
  `session_info → get_windows → find_elements → click` = 4 calls
  minimum, 5 if the agent needs `inspect_element` to pick between
  `wpf_click` and `wpf_execute_command`.
- **R-C §1**: Same concern — `wpf_get_session_info` eats one of the 20
  cap slots while being a pure meta-tool.
- **Resolution**: `wpf_get_session_info` returns `{session, windows[]}`
  inline (top-level windows with locators, no children). `wpf_get_windows`
  becomes a specialisation for refreshing the window list.
  `wpf_find_elements` result items must include `hasCommandBinding: bool`
  so the agent decides L0 vs L1 without an extra `inspect_element` call.

### W3-C3 — Semantic-navigation tools don't survive contact with real MC code

- **R-C §4**: Spot-checked `SessionVM`, `DebugViewerVM`, interface
  hierarchy:
  - `wpf_find_by_viewmodel`: MC's DataContext is predominantly
    interface-typed (`ISessionVM`, not `SessionVM`). PRD's "short-name
    match" assumes concrete types.
  - `wpf_trace_command`: MC uses the `ReactiveCommand ??=` lazy pattern
    (e.g. `_windowClosingCommand` in `DebugViewerVM` is null until first
    bind). Reverse-index returns nothing for uninstantiated commands.
  - `wpf_resolve_binding`: works — DP-to-VM-property chains are clean.
- **R-D #3**: `wpf_find_by_viewmodel` on a 500-row virtualized session
  list needs scroll-and-materialize cycles (items not in visual tree
  until scrolled in). Works on a demo, fails on production data.
- **Resolution**: Drop `wpf_trace_command` from MVP — lazy
  `ReactiveCommand` instantiation is a MC-pattern reality that defeats
  the reverse-index. Move `wpf_find_by_viewmodel` to v1.1 pending a
  spike on interface-typed DataContext resolution + virtualized-list
  materialisation. Keep `wpf_resolve_binding`. The "5 winning things"
  #5 (semantic moat) reduces to one tool, not three, in MVP.

### W3-C4 — Schedule is 50–80% optimistic, MC integration is the silent critical path

- **R-D**: P50 18–22 weeks, P90 26–32 weeks vs PRD's 11–17. Critical
  path is MC integration (5–7 weeks realistic vs PRD's 3–4) due to the
  admitted 30% FlaUI coverage gap (§12.3: slider, virtualized scroll,
  `ResetToHome` state machine, license dialog, hardware readiness,
  multi-product launch).
- **R-D**: "Parallel M2 + MC integration tracks" is a 1.5-engineer
  fiction on a 1-engineer team — they serialise, adding 2–4 weeks.
- **R-C**: Proposes a different solve — a 10-tool MVP shipping in 7–9
  weeks, then v1.1 adds the rest.
- **Resolution**: Two options, user decides:
  - **Option A (R-C path)**: Cut to 10 tools, 2 milestones, 7–9 weeks;
    defer L1 act tools + `wpf_find_by_viewmodel` +
    `wpf_trace_command` + screenshot + ancestors/children pagination to
    v1.1.
  - **Option B (honest 20-tool path)**: Keep the 20-tool surface but
    revise the estimate to 18–22 weeks P50, 26–32 P90, drop the
    "parallel" framing, explicitly gate M2 close on specific MC
    scenarios rather than a percentage.

---

## New security findings (beyond Wave-2 MF-1..MF-9)

### MF-10 — `ToString()` override redaction bypass (R-A #4)

**Severity**: High.

`RedactionFilter` (RedactionFilter.cs:17–40) checks *property names*
against 21 keywords. The value comes from `DtoProjection.ToPropertyDto`
→ `prop.StringValue` → `prop.Value?.ToString()`. A DP whose type
overrides `ToString()` to embed credentials (e.g. a
`ConnectionStringBuilder`-like VM property named `DatabaseConfig`) leaks
in full: property name doesn't match any keyword, but the rendered
string is the full connection string with password.

**Fix before**: M1. `Redact()` must inspect the *runtime type* of
`prop.Value` against a structural-sensitivity map (include
`SecureString`, `NetworkCredential`, types deriving from
`DbConnectionStringBuilder`, types with `[Sensitive]` marker attribute)
before invoking `ToString()`. On match, substitute the redaction
sentinel regardless of property name.

### MF-11 — Injection mode does not enforce `EnableRedaction: true` (R-A #8)

**Severity**: Critical.

§4.1 and §9.7 say injection mode is "inspection only, L0/L1 refused,"
but the session-policy table in §4.3 only gates `EnableMutation` and
`EnableAutomation`. `EnableRedaction` defaults to the caller's
`SnoopAgentOptions`. An agent injected into a third-party app (e.g. a
password manager) with redaction disabled via command-line flag gets
`wpf_get_properties` on every element with full values — the 9 Observe
tools are all enabled in injection mode.

**Fix before**: M1. `InputStrategySelector` (or equivalent session-policy
gate) must force `EnableRedaction = true` unconditionally when
`IntegrationMode == Injection`. This is a policy choice, not a caller
default.

---

## New code bugs (beyond §14's six)

### Bug #7 — `CursorManager.GetPage` non-atomic `Offset` RMW race (R-A #1)

`CursorEntry.Offset` is a plain `int` read-modify-write without a lock.
Two MCP transport threads calling `wpf_get_children` with the same
cursor token concurrently can both read `offset=0`, compute `end=20`,
write `Offset = 20`, return duplicate pages. Worse: at the "all consumed"
branch (CursorManager.cs:117) one thread can remove the snapshot entry
while the other is mid-read.

**Fix**: Lock on `CursorEntry` during `GetPage`, or use
`Interlocked.CompareExchange` on `Offset`. Document cursor tokens as
single-consumer in §8.3.

### Bug #8 — `NodeRegistry.Clear()` counter reset without drain (R-A #7)

`counter = 0` (non-atomic assignment) at NodeRegistry.cs:132 while
`Interlocked.Increment` is used elsewhere. If a Dispatcher callback is
in-flight past the concurrency semaphore during session reset, it can
register `0:1`, `0:2` into `reverse`, which `ClearForwardTable()` on
.NET 4.6.2 (guarded by `#if NET6_0_OR_GREATER` at line 138) does not
clear. Next session's counter starts from 0, collides with stale entries.

**Fix**: `Clear()` must drain the concurrency semaphore first. Counter
reset via `Interlocked.Exchange(ref counter, 0)`. `#if` guard on
`ClearForwardTable` is either wrong or the method needs a non-.NET-6
fallback.

### Bug #9 — `WpfLocator` re-resolution creates unbounded NodeRegistry growth (R-A #9)

PRD §6 markets `WpfLocator` re-resolution as the stale-handle fix. But
`wpf_find_by_viewmodel` resolution walks DataContext across the whole
tree, calling `nodeRegistry.GetOrCreateId` for every match. On a
500-row session list with three agents polling at the §11 metric #2
target (p95 < 10 ms), NodeRegistry accumulates 500 entries per poll
cycle. The 10,000-entry sweep threshold (NodeRegistry.cs:91) fires on a
`Timer` thread without locking `reverse` — concurrent `GetOrCreateId`
during sweep can observe a partially-pruned dictionary.

**Fix**: Cap entries created per locator-resolution call. Sweep timer
must take the same lock as `GetOrCreateId` (or both must be lock-free
with CAS, which the current code is not). Consider a per-locator
resolution cache with short TTL.

### Bug #10 — Audit log HMAC chain spec missing (R-A #10)

§9.4 says "monotonic sequence, per-entry HMAC" but never specifies
whether the chain is stateful (each entry's HMAC includes the prior
entry's hash) or per-entry-independent. If chained (required for
tamper-evidence), concurrent writes from MCP transport thread + mutation
callback corrupt the chain. If not chained, entries are individually
forgeable by splicing.

**Fix**: Specify: single dedicated writer (`Channel<AuditEntry>` funnel),
HMAC-SHA256 keyed with session key including monotonic counter as nonce,
key derived from `CryptGenRandom` at session start (not from process
identity). Write this into §9.4 explicitly.

---

## Concurrency / specification hazards

### W3-H1 — `DispatcherPriority.Send` starves `ApplicationIdle` (R-A #5)

`RunOnDispatcherAsync` dispatches at `DispatcherPriority.Send`
(SnoopInspector.cs:1362), the highest WPF priority. Three concurrent
tool calls (the semaphore limit) + a tight animation loop can prevent
`ApplicationIdle` (`Background` priority) from firing. `DispatcherIdling
Resource` in the AND-gate never reports idle → `wpf_pump_until_idle`
spins until the 5 s `AnimationRunawayException` ceiling.

**Fix**: §8.2 must specify at which Dispatcher priority each idle
resource polls. `DispatcherIdlingResource` uses `ContextIdle` (above
`ApplicationIdle`, below `Send`). Document that tool dispatch and idle
gating run at compatible priorities.

### W3-H2 — Circular test dependency: `wait_for_property` ↔ `poll_changes` (R-A #6)

Both tools live in §5.4 as "utility." `wpf_wait_for_property` is
described as the primary sync primitive; it's implementable as a loop
over `wpf_poll_changes`. Integration-testing either requires the other.
M0 spikes S-3 and S-5 validate the change-feed plumbing but neither has
an exit criterion that proves mutation → poll-response round-trip
without leaning on `wait_for_property`.

**Fix**: Add M0 spike S-3b: "Single test proves mutation-to-poll-response
cycle without using `wpf_wait_for_property`. Uses `Thread.Sleep` +
`wpf_poll_changes` only, measured against expected tree-version delta."

### W3-H3 — Path locator is not durable (R-A #2)

§6 describes `{ "$locator": "path=Window\\Grid\\StackPanel\\Button" }`
as "durable" with "zero stale-handle failures." But `FindByPathSegments`
(SnoopInspector.cs:1494–1523) takes the first child whose type name
matches each segment. Three sibling Buttons in a StackPanel → path
always resolves to the first one. Sibling reordering silently changes
the target.

**Fix**: Remove "durable" from §6 for `path=`. Document explicitly that
`path=` locators are unstable under sibling reordering and should be
avoided; prefer `automationId=` and `viewModel=`. Consider deprecating
`path=` behind an analyzer warning.

---

## Agent-consumer ergonomics (R-B)

### W3-E1 — Five act tools create systematic naming ambiguity

Five scenarios where an LLM will pick wrong:

| Scenario | Agent instinct | Should use | Risk |
|----------|---------------|-----------|------|
| Click Save button with ICommand binding | `wpf_click` (L1) | `wpf_execute_command` (L0) | Overhead, brittleness |
| Check a checkbox already in unknown state | `wpf_toggle` | `wpf_set_check_state` | Flips to wrong state |
| Select ComboBox item | `wpf_expand_collapse` + `wpf_click` | `wpf_select_item` | Two extra calls |
| Check ToggleButton (not CheckBox) | `wpf_set_check_state` | `wpf_toggle` | Works but is wrong |
| Click MenuItem with IsCheckable | ambiguous | depends on state | Wrong tool 50% |

**Fix**: Every Act tool description must include:
- Purpose (1 sentence)
- Guidelines ("prefer X over Y when Z")
- Limitations ("does not handle A")
- Typed exclusion list ("use on: CheckBox, RadioButton. Not for:
  ToggleButton")

This is the arxiv:2602.14878 four-component pattern. PRD §5.2 has only
Purpose. Expected lift: +5.85pp task success, +15.12% evaluator
performance (SOTA §1.5).

### W3-E2 — Missing primitives

Four common workflows the 20 tools cannot cleanly express:

1. **Verify modal dismissed**: needs `elementPresent: false` in state
   delta when target disappears post-action, OR `wpf_wait_for_property`
   accepting `expectedValue: null` meaning "wait for absence".
2. **Scroll to item without selecting**: `wpf_select_item` has the
   side-effect of selection. No pure-scroll primitive.
3. **Wait for progress bar complete**: progress bars use
   `IsIndeterminate`; no tool expresses "wait until determinate-100 OR
   element hides itself".
4. **Negative existence assertion**: `wpf_find_elements` returning
   empty is not distinguishable from error. Needs `mustNotExist: true`
   flag or dedicated `wpf_assert_absent`.

**Fix**: Add a `presenceExpected: present|absent` parameter to
`wpf_wait_for_property` covering (1) and (4). Evaluate (2) and (3)
against MC scenarios — if any break, add; else document as v1.1.

---

## Delivery realism (R-D)

### W3-D1 — Codebase is further along than the PRD claims, which *changes* spike priorities

- snoopwpf is ~45K LOC, 8 projects, 15+ working tool handlers, 2,779
  LOC integration-test harness, 1,930-line `SnoopInspector.cs`.
- S-1 (stdio transport) is already passed — integration-test harness
  proves it.
- S-3 (tree-change detection) is *partially* built (NodeRegistry +
  RedactionFilter), but has never been benchmarked against the
  "< 1 µs/mutation on a 200-node tree with 1e6 bumps" target. This is
  the real binary-outcome spike. If a `Panel.Children.CollectionChanged`
  synchronous fire costs 3–5 µs, the four-source feed needs a coalescing
  ring buffer — a new subsystem not in MVP scope.
- S-2 (UnsafeAccessor) is academic in MVP (L3/L4 deferred).
- S-5 (`ValueChangedEventManager` leak) must run 1e6 cycles before M1.

**Fix**: Rewrite §10 M0 to reflect actual spike costs. S-1 and S-2 are
free. S-3 is the one that matters. S-5 is 2–3 days.

### W3-D2 — Hidden work not in §13's 20 user stories

- VeriGUI-style test harness for the M2 gate (< 5% repeat-on-unchanged
  rate across 100 scenarios). Not built, not a user story.
- CI dual-stack matrix (net6/8/9, x64): 3–5 days invisible work.
- NuGet packaging + signing + feed decision for
  `SnoopWPF.Agent.Shim.FlaUI`: not mentioned.
- The 30% FlaUI coverage gap is a ship blocker, not an "acceptable
  gap." M2 gate requires 18 scenarios pass headlessly; if shim doesn't
  cover them, either gate definition is wrong or scenarios are
  excluded.

**Fix**: Add §13 stories for: `US-VERIGUI-HARNESS`,
`US-CI-DUAL-STACK`, `US-NUGET-PACKAGING`, `US-COVERAGE-GAP-AUDIT`.
Audit the 30% gap against the 18 scenarios *before* M1 starts.

### W3-D3 — Top-3 actions before M1 starts

1. **Run the S-3 benchmark on real snoopwpf code** against a 200-node
   WPF tree with 1e6 mutation cycles. Answer: µs/bump. This is the one
   result that could force an architectural rework. Know it now.
2. **Audit 18 active MC SpecFlow scenarios against the §12.3 30% gap**.
   Map each gap item to a specific scenario. Decide: shim covers /
   FlaUI stays / descoped. If 5+ need FlaUI fallback, rewrite M2 gate
   before starting M2.
3. **Wire headless CI for the Snoop suite end-to-end before any M1
   code**. Existing `IntegrationTestFixture.cs` + `TestWpfApp.cs`
   running on CI matrix (net6/8, x64). 2–3 days, pays back throughout
   M1+M2.

---

## Recommendation: PRD revision before M1

Wave 3 surfaces enough concrete spec gaps, new security findings, new
code bugs, and ergonomics issues that PRD-v5-MVP.md should be revised
before M1 starts, not iteratively patched during M1.

**Suggested revision scope (keep MVP doc at ~700 lines, not a rewrite):**

1. §7 state delta: rewrite with the explicit `failureReason` enum,
   `suggestion: {tool, args}` schema, computation-timing rule, reduced
   default field set.
2. §5.2 tool descriptions: add Guidelines + Limitations + typed
   exclusions per tool (arxiv:2602.14878 format).
3. §5.1 `wpf_get_session_info`: returns top-level windows inline.
4. §5.1 `wpf_find_elements`: result includes `hasCommandBinding`.
5. §5.1 drop `wpf_trace_command`; move `wpf_find_by_viewmodel` to
   v1.1 with rationale. This is 18-tool MVP, still under the SOTA cap.
6. §6 remove "durable" claim from `path=` locators.
7. §8.2 specify Dispatcher priorities for idle resources.
8. §9.4 write the HMAC chain spec.
9. §9.2 add MF-10 (`ToString()` bypass) + MF-11 (injection
   `EnableRedaction` unconditional).
10. §10 rewrite M0 spike priorities. S-3 is the real blocker. S-1
    passed. S-2 academic.
11. §11 fix metric #1 to reflect bootstrap chain — or guarantee
    `wpf_get_session_info` returns windows so 3-call target is
    reachable.
12. §13 add the four missing user stories (VeriGUI harness, CI dual-
    stack, NuGet packaging, coverage-gap audit).
13. §14 append bugs #7–10.
14. §10 schedule: either revise to P50 18–22 wk or re-scope to R-C's
    10-tool path at 7–9 wk. User decision.

The 20-tool surface is not wrong. The spec is just vaguer than it
needs to be, the security surface missed two attack vectors, and the
schedule is optimistic on MC integration. None of these require
re-architecting — all are targeted edits. Do them before M1 code starts.
