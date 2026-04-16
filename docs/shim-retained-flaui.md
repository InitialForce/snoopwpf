# FlaUI-Retained Scenarios

**Bead:** M2-16 (bd-2co) — Coverage-gap closure per PR-1 outcome  
**Audit source:** COVERAGE-GAP-AUDIT.md (M0-06, bd-3s4)  
**Date:** 2026-04-16

---

## Overview

The M0-06 coverage-gap audit evaluated 18 active MC SpecFlow scenario families against the
v5-MVP shim tool surface.  Two families cannot be fully migrated to the shim in the current
milestone and must retain a `FlaUIActionCatalog` fallback.  This document records those
families, the blocking reason, and the expected resolution path.

The active FlaUI fallback count is **2**, which is below the M2 gate threshold of 5.
**M2 gate definition stands as written.**

---

## Retained Families

### SCENARIO FAMILY 13 — Analysis/Explorer

**Feature file:** `Features/Analysis/Explorer.feature`  
**Scenarios:** 01 Opens and shows student list, 02 Shows downloadable content, 03 Close returns to Analysis

**Blocking reason:** Scenario 01 (student list) directly exercises the unfiltered 500+ item
virtualized list path.  The shim's `wpf_select_item` uses a `ScrollIntoView` + `SelectedItem`
assignment workaround (§5.2) which is reliable for *selecting* an item but does not cover
*asserting all visible items* without selection side-effects.  The inspection-without-selection
path (required for "student list shows all items" assertions) is flaky with the current shim
primitives per MC §8 item 2.

**Why scenarios 02 and 03 are also retained:** They are in the same feature file and run as
part of the same SpecFlow scenario context.  Splitting them into separate catalogs would require
MC-side step-definition changes outside this bead's scope.

**Resolution path:** Address in v1.1 when the W3-E2 fix for virtualized inspection-without-
selection lands, or when MC migrates to a dedicated `wpf_inspect_visible_items` primitive.

---

### SCENARIO FAMILY 15 — Analysis/Capture

**Feature file:** `Features/Analysis/Capture.feature`  
**Scenarios:** 01–05 (SC/MC Capture with user, MC with activity, Manual trigger, Microphone trigger)

**Blocking reason:** Scenarios 04 (Manual trigger) and 05 (Microphone trigger) require
completion of the full recording pipeline and `WaitForEncoding` polling — a multi-step async
state machine that `wpf_pump_until_idle` alone cannot cover.  The shim has no equivalent of
FlaUI's `AppSession.WaitForEncoding(timeout)` which polls internal pipeline state.

Additionally, branding baseline management (CI artifact diffing in scenarios 01–03) requires
MC-side CI infrastructure (artifact storage + diff runner) that is deferred to MC-1 CI work.
While `wpf_capture_screenshot` captures the image, the diff pipeline is not shim-side.

**Resolution path:** Address encoding pipeline polling in v1.1 (new broker lifecycle tool
`mc_wait_for_recording` or equivalent).  Branding baseline infrastructure is a separate MC-1
CI track.

---

## Descoped Families (Hardware-Gated, No Active Scenarios)

The following families have no active scenarios in CI and are therefore excluded from the
active FlaUI fallback count.  They are listed here for completeness.

| Feature file | Reason |
|---|---|
| `Features/Settings/Cameras.feature` | Hardware-gated: requires physical camera enumeration |
| `Features/Assessment/Capture.feature` | Hardware-gated: all scenarios commented out |
| `Features/LaunchMonitor.feature` | Hardware-gated: launch monitor requires physical device |

These families run only in the hardware lab and are out of scope for shim migration until a
suitable mock is available.

---

## Gate Verification

```bash
# Count active FlaUI-retained families (must be < 5 for M2 gate to hold)
grep -cE "^### SCENARIO FAMILY" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
# → 18 total families

grep "Decision.*FlaUI" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
# → SCENARIO FAMILY 13 (Analysis/Explorer)
# → SCENARIO FAMILY 15 (Analysis/Capture)
# Active FlaUI fallback count = 2. Gate threshold = 5. Gate holds.
```
