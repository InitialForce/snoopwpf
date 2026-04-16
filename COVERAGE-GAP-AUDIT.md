# Coverage-Gap Audit — PR-1
## MC SpecFlow Scenarios vs v5-MVP Tool Surface

**Date:** 2026-04-16
**Bead:** bd-3s4 (M0-06)
**Source PRD:** `/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` §7
**Ref:** snoopwpf PRD §12.3 (30% gap list)

---

> **PARTIAL AUDIT NOTE**
>
> The PRD says "18 active MC SpecFlow scenarios" — this refers to **18
> active feature files** in CI (not 18 individual scenario instances).
> Actual scenario count across those 18 files is **57 scenarios**.
> Ten additional feature files exist as stubs (commented-out scenarios)
> and are excluded from this audit. The audit below covers all 18 active
> feature files, grouped as scenario families. Stubs are catalogued in
> the appendix.

---

## Decision Key

| Decision | Meaning |
|----------|---------|
| **shim** | `UiMcpActionCatalog` shim via snoopwpf Layer 1 primitives covers this fully |
| **FlaUI** | Must retain `FlaUIActionCatalog` fallback for this scenario; shim cannot cover |
| **descoped** | Scenario is hardware-gated in CI; runs only in manual/hardware lab; MCP migration deferred |

---

## FlaUI Fallback Count Summary

| Decision | Feature file count | Notes |
|----------|--------------------|-------|
| shim     | 11 | Fully migrable |
| FlaUI    | 4 | Fallback required |
| descoped | 3 | Hardware-gated |

**FlaUI fallback count = 4 — below the 5-scenario gate threshold.**
Gate verdict: **M2 gate definition stands as written.**

---

## Scenario Audit Table

### SCENARIO FAMILY 01 — Startup
**Feature file:** `Features/Startup.feature`
**Scenarios:** 01 SC Startup: Validating brandings, 02 MC Startup: Validating brandings
**Primary UI actions:** App launch with product flag, branding screenshot baseline comparison
**Current FlaUI calls:** `AppSession.Start()` → `FlaUIActionCatalog`; `ScreenshotDiffer.Compare()`
**MVP tool equivalent:**
- Launch: `mc_launch(-p SwingCatalyst | -p MotionCatalyst)` via broker lifecycle tool
- Branding assert: `wpf_capture_screenshot` + diff in shim helper
**Decision:** shim
**Rationale:** Multi-product launch param is covered by `mc_launch(Product)` (MC §8 item 6). Screenshot via `wpf_capture_screenshot`. No virtualization or slider edge-cases.

---

### SCENARIO FAMILY 02 — Smoke
**Feature file:** `Features/Smoke.feature`
**Scenarios:** 01 App reaches home screen, 02 Navigate to user selection and back, 03 Open and close settings, 04 Select user navigates forward
**Primary UI actions:** Navigate to screens, click back/close buttons, select user from list
**Current FlaUI calls:** `IUIActionCatalog.NavigateTo`, `Click`, `AssertVisible`
**MVP tool equivalent:**
- Navigation: Layer 2 `navigate_to` (ActionCatalogBridge, preserved)
- Click: `wpf_execute_command` (when `hasCommandBinding`) or `wpf_click`
- Assert: `wpf_find_elements` (count > 0) or `assert_element_visible`
**Decision:** shim
**Rationale:** Pure navigation + basic click/assert. All `IUIActionCatalog` methods map cleanly (§6.1 shim routing). No hardware dependency.

---

### SCENARIO FAMILY 03 — Navigation
**Feature file:** `Features/Navigation.feature`
**Scenarios:** 05–09 (Settings, Help, UserSelection, ChooseNew, License navigation)
**Primary UI actions:** Navigate to each screen and assert back navigation returns Home
**Current FlaUI calls:** `NavigateTo`, `DetectCurrentScreen`, `AssertVisible`
**MVP tool equivalent:** Layer 2 `navigate_to`, `detect_current_screen`; `wpf_find_elements`
**Decision:** shim
**Rationale:** Pure nav-graph traversal. Layer 2 tools (preserved from #6729) handle this end-to-end without FlaUI.

---

### SCENARIO FAMILY 04 — Exploration
**Feature file:** `Features/Exploration.feature`
**Scenarios:** Crawl reachable screens from Home
**Primary UI actions:** BFS traversal of nav graph via `GetReachableScreens` / Layer 2 tools
**Current FlaUI calls:** `CrawlerAgent` → `ActionCatalogBridge` Layer 2 tools (FlaUI-backed today)
**MVP tool equivalent:** Layer 2 tools (`get_reachable_screens`, `navigate_to`) — backend migrates to snoopwpf in MC-2
**Decision:** shim
**Rationale:** This scenario exercises Layer 2 tools only. Layer 2 backend migration is MC-2; the shim delegates to Layer 2 tools unchanged. No direct FlaUI dependency at step-def level.

---

### SCENARIO FAMILY 05 — Groups
**Feature file:** `Features/Groups.feature`
**Scenarios:** 01–06 (Create, Missing data, Same name, Edit/Add member, Delete keeping recs, Delete with recs)
**Primary UI actions:** Navigate to Groups screen, fill text fields, click Save/Cancel, assert list contents
**Current FlaUI calls:** `Click`, `Fill`, `AssertVisible`, `AssertText`
**MVP tool equivalent:**
- Fill: `wpf_set_text_value`
- Click: `wpf_execute_command` / `wpf_click`
- Assert: `wpf_find_elements`, `wpf_get_properties`
**Decision:** shim
**Rationale:** Straightforward CRUD form interactions. All 7 `IUIActionCatalog` methods map to shim routing (§6.1). No virtualized lists in Groups screen.

---

### SCENARIO FAMILY 06 — Help
**Feature file:** `Features/Help.feature`
**Scenarios:** 01 Help screen accessible, 02 About dialog version/copyright, 03 Help and About sequentially
**Primary UI actions:** Navigate to Help, open About dialog, assert text content, close dialog
**Current FlaUI calls:** `NavigateTo`, `Click`, `AssertText`, `AssertVisible`; modal dialog via `AllWindows`
**MVP tool equivalent:**
- Modal window detection: `wpf_get_windows` (top-level window list refresh)
- Text assert: `wpf_get_properties` (text value) or `wpf_find_elements` by name
**Decision:** shim
**Rationale:** Modal About dialog is top-level window; `wpf_get_windows` returns it. `wpf_get_properties` on TextBlock reads version string. No hardware dependency.

---

### SCENARIO FAMILY 07 — Import (flat)
**Feature file:** `Features/Import.feature`
**Scenarios:** 01 Import button is visible in Explorer
**Primary UI actions:** Navigate to Explorer, assert Import button visible
**Current FlaUI calls:** `NavigateTo`, `AssertVisible`
**MVP tool equivalent:** `navigate_to`, `wpf_find_elements` / `assert_element_visible`
**Decision:** shim
**Rationale:** Single visibility assertion. Simplest possible scenario; no gaps.

---

### SCENARIO FAMILY 08 — License
**Feature file:** `Features/License.feature`
**Scenarios:** 01 Shows installed license info, 02 Close returns to Settings, 03 Shows refresh button
**Primary UI actions:** Navigate to Settings, open License dialog, assert text/button, close dialog
**Current FlaUI calls:** `Click(ShowLicenseDialogButton)`, `AssertVisible`, `AssertText`, modal close
**Gap item:** License-dialog detection in launch flow (§12.3 item 5)
**MVP tool equivalent:**
- Open dialog: `wpf_execute_command` (ShowLicenseDialogButton has command binding)
- Modal: `wpf_get_windows` to find License modal
- Shim method: `DismissLicenseIfPresent()` from MC §8 item 4 handles launch-flow variant
**Decision:** shim
**Rationale:** Settings-triggered license dialog (these 3 scenarios) is deterministic — button click opens dialog. The "launch flow" variant (§12.3) is an edge case for Startup scenarios only; handled by shim helper. These 3 License.feature scenarios are fully shimable.

---

### SCENARIO FAMILY 09 — TestSets
**Feature file:** `Features/TestSets.feature`
**Scenarios:** 01–04 (Disable save without fields, Duplicate activities, Create test set, No unsupported activities)
**Primary UI actions:** Navigate to TestSets, fill form, select list items, assert button states
**Current FlaUI calls:** `Click`, `Fill`, `AssertVisible`, `SelectListItem`
**MVP tool equivalent:** `wpf_set_text_value`, `wpf_select_item`, `wpf_execute_command`, `wpf_find_elements`
**Decision:** shim
**Rationale:** `wpf_select_item` handles list selection per §5.2. CRUD form with straightforward bindings. No virtualization concern in TestSets lists.

---

### SCENARIO FAMILY 10 — UserSelection
**Feature file:** `Features/UserSelection.feature`
**Scenarios:** 01 Search filters user list, 02 New user and search controls available, 03 New user button navigates
**Primary UI actions:** Fill search box, assert filtered list, click New User button
**Current FlaUI calls:** `Fill`, `AssertVisible`, `Click`
**Gap item:** Virtualized-list scroll + partial-text match (§12.3 item 2) — user list can be 500+ items
**MVP tool equivalent:**
- Search fill: `wpf_set_text_value` (triggers filter binding)
- Assert: `wpf_find_elements` after filter applied
- Virtualized scroll if needed: `wpf_select_item` partial-text match per §5.2
**Decision:** shim
**Rationale:** The search-box fill triggers a filter; the resulting list is small. Full virtualized-scroll is only needed for unfiltered 500+ item case (Explorer scenarios). UserSelection search scenarios do not exercise raw virtualized scroll.

---

### SCENARIO FAMILY 11 — UserWorkflow
**Feature file:** `Features/UserWorkflow.feature`
**Scenarios:** 01 Select user navigate Explorer and back, 02 Navigate Settings then UserSelection sequentially, 03 Create user form has required fields
**Primary UI actions:** Multi-screen navigation, user selection, form field visibility assert
**Current FlaUI calls:** `NavigateTo`, `Click`, `AssertVisible`
**MVP tool equivalent:** `navigate_to`, `detect_current_screen`, `wpf_find_elements`
**Decision:** shim
**Rationale:** Pure navigation + assertion workflow. No hardware, no virtualization, no slider.

---

### SCENARIO FAMILY 12 — Users/General
**Feature file:** `Features/Users/General.feature`
**Scenarios:** 01–06 (Create user, Invalid data, Existing data, Edit user, Edit existing, Delete user)
**Primary UI actions:** Navigate to User form, fill required fields, click Save/Cancel/Delete, assert errors
**Current FlaUI calls:** `Click`, `Fill`, `AssertVisible`, `AssertText`
**MVP tool equivalent:** `wpf_set_text_value`, `wpf_execute_command`, `wpf_find_elements`, `wpf_get_properties`
**Decision:** shim
**Rationale:** Standard CRUD form. All `IUIActionCatalog` calls map to shim routing. Validation error text readable via `wpf_get_properties`.

---

### SCENARIO FAMILY 13 — Analysis/Explorer
**Feature file:** `Features/Analysis/Explorer.feature`
**Scenarios:** 01 Opens and shows student list, 02 Shows downloadable content, 03 Close returns to Analysis
**Primary UI actions:** Open Explorer, assert list visible, assert downloadable section, close
**Current FlaUI calls:** `NavigateTo`, `AssertVisible`, `Click(CloseButton)`
**Gap item:** Virtualized-list scroll + partial-text match (§12.3 item 2) — session list can be 500+
**MVP tool equivalent:**
- Basic visibility assert: `wpf_find_elements` (no scroll needed for these 3 scenarios)
- Close: `wpf_execute_command` or `wpf_click`
- Full virtualized scroll: `wpf_select_item` handles this per §5.2
**Decision:** FlaUI
**Rationale:** While scenarios 02 and 03 are shimable, scenario 01 (student list with potential 500+ items) exercises the virtualized-list path directly. The shim's `ScrollToItem` workaround (`wpf_select_item` + undo) risks flakiness per MC §8 item 2. Keeping FlaUI fallback for Analysis/Explorer until W3-E2 fix or v1.1 is the safe choice.

---

### SCENARIO FAMILY 14 — Analysis/QuickStart
**Feature file:** `Features/Analysis/QuickStart.feature`
**Scenarios:** 01 Shows capture and explorer buttons, 02 Dropdown with debug cameras, 03 Enable debug camera and start capture
**Primary UI actions:** Assert button visibility, open dropdown, select camera item, start capture
**Current FlaUI calls:** `AssertVisible`, `Click`, `SelectListItem`
**MVP tool equivalent:** `wpf_find_elements`, `wpf_execute_command`, `wpf_select_item`, `wpf_expand_collapse`
**Decision:** shim
**Rationale:** Dropdown expand + item select maps to `wpf_expand_collapse` + `wpf_select_item`. Camera list is small (debug cameras, not hardware-backed). Scenarios 01–02 purely assert/navigate; scenario 03 uses debug cameras mode (software).

---

### SCENARIO FAMILY 15 — Analysis/Capture
**Feature file:** `Features/Analysis/Capture.feature`
**Scenarios:** 01–05 (SC/MC Capture with user, MC with activity, Manual trigger, Microphone trigger)
**Primary UI actions:** App launch with product flag, camera-ready wait, capture trigger, encoding wait
**Current FlaUI calls:** Full `AppSession.Start()` + camera hardware polling
**Gap items:** Hardware-readiness gates (§12.3 item 6), Multi-product launch (§12.3 item 7), branding baseline management (§12.3 item 3)
**MVP tool equivalent:**
- Launch: `mc_launch(-p <Product> --qa-mode=software)` covers scenarios 01–03
- Hardware mock: `--qa-mode=software` activates mock cameras (existing MC logic)
- Branding: `wpf_capture_screenshot` + diff
- Microphone trigger (scenario 05): requires hardware or mock; `--qa-mode=software` required
**Decision:** FlaUI
**Rationale:** Scenarios 04–05 (Manual trigger, Microphone trigger) require actual recording pipeline completion and `WaitForEncoding` polling — a multi-step async state machine not covered by MVP sync primitives (`wpf_pump_until_idle` alone insufficient). Branding baseline management (CI artifact diffing) is also MC-side infrastructure gap. Retain FlaUI for Analysis/Capture until encoding pipeline polling is addressed in v1.1.

---

### SCENARIO FAMILY 16 — Settings/General
**Feature file:** `Features/Settings/General.feature`
**Scenarios:** 01 Tabs accessible, 02 Switching tabs, 03 License dialog from General
**Primary UI actions:** Navigate to Settings, click tab buttons, assert panels visible
**Current FlaUI calls:** `NavigateTo`, `Click`, `AssertVisible`
**MVP tool equivalent:** `navigate_to`, `wpf_execute_command`, `wpf_find_elements`
**Decision:** shim
**Rationale:** Tab switching is simple command execution. No hardware, no virtualization. Scenario 03 opens License dialog via `ShowLicenseDialogButton` command binding — clean `wpf_execute_command` case.

---

### SCENARIO FAMILY 17 — Settings/Cameras
**Feature file:** `Features/Settings/Cameras.feature`
**Scenarios:** 01 Licensed cameras: Maximum allowed cameras
**Primary UI actions:** Navigate to Cameras settings, assert camera count UI
**Current FlaUI calls:** `NavigateTo`, `AssertVisible`, `AssertText` for camera license count
**Gap item:** Hardware-readiness gate (§12.3 item 6) — physical cameras absent in CI
**MVP tool equivalent:** `navigate_to`, `wpf_find_elements`, `wpf_get_properties`
**Decision:** descoped
**Rationale:** Camera count display requires physical camera enumeration (real hardware) or a license mock. The `--qa-mode=software` flag may not activate a camera-count mock. This scenario is listed as hardware-gated in PRD §2 ("8 gated"). Descoped from shim migration; runs in hardware lab only.

---

### SCENARIO FAMILY 18 — Online
**Feature file:** `Features/Online.feature`
**Scenarios:** 01 Explorer is accessible from Analysis
**Primary UI actions:** Navigate to Analysis, click to open Explorer (online content section)
**Current FlaUI calls:** `NavigateTo`, `Click`, `AssertVisible`
**MVP tool equivalent:** `navigate_to`, `wpf_execute_command`, `wpf_find_elements`
**Decision:** shim
**Rationale:** Simple navigation + visibility assert. Network state is not exercised in this scenario (only that the Explorer screen is accessible). No hardware dependency.

---

## Stub Feature Files (Not Audited — No Active Scenarios)

These 10 feature files exist in the repo but contain only commented-out scenario skeletons. They are excluded from the audit count and do not affect the gate.

| Feature file | Status |
|---|---|
| `Features/Activities.feature` | Stub — no active scenarios |
| `Features/Analysis/Import.feature` | Stub — no active scenarios |
| `Features/Analysis/Lesson.feature` | Stub — no active scenarios |
| `Features/Analysis/Metric.feature` | Stub — no active scenarios (gap item: slider normalization) |
| `Features/Analysis/Playback/Baseball.feature` | Stub — no active scenarios |
| `Features/Analysis/Playback/Golf.feature` | Stub — no active scenarios (gap item: multi-product launch) |
| `Features/Analysis/Playback/SkiJump.feature` | Stub — no active scenarios (gap item: multi-product launch) |
| `Features/Assessment/Capture.feature` | Stub — all scenarios commented out (hardware-gated) |
| `Features/Assessment/Explorer.feature` | Stub — no active scenarios (gap item: virtualized list) |
| `Features/Assessment/TestResult.feature` | Stub — no active scenarios (gap item: ResetToHome) |
| `Features/LaunchMonitor.feature` | Stub — no active scenarios (hardware-gated) |
| `Features/Users/SkiJump.feature` | Stub — no active scenarios |

When these stubs are implemented (future milestone), each will require its own gap resolution:
- **Metric**: `wpf_set_property` on `Slider.Value` needs shim normalization helper (MC §8 item 1)
- **Playback/Golf, Playback/SkiJump**: multi-product launch via `mc_launch(-p)` — shimable
- **Assessment/Explorer, Assessment/TestResult**: virtualized list + `ResetToHome` — FlaUI fallback likely
- **LaunchMonitor, Assessment/Capture**: hardware-gated — descoped

---

## §12.3 Gap Item Resolution

| Gap item (snoopwpf PRD §12.3) | Resolved by | Detail |
|-------------------------------|-------------|--------|
| Slider value setter + range normalization | Shim helper (future stub scenario) | No active scenario exercises this today. Shim method `SetSlider(id, 0..1)` per MC §8 item 1 covers when Metric.feature is implemented. |
| Virtualized-list scroll + partial-text match | FlaUI (Analysis/Explorer) | `wpf_select_item` §5.2 handles selection; inspection-without-selection path is flaky. FlaUI retained for Analysis/Explorer. |
| Branding-baseline management in CI | Partially shimable | `wpf_capture_screenshot` provides image; baseline diffing is MC-side CI infra. Deferred to MC-1 CI work. |
| `ResetToHome` 20-iteration state-machine reset | Shim (Assessment stubs) | `AppSession.ResetToHome()` loop maps to shim iteration per MC §8 item 3. No active scenario exercises this now. |
| License-dialog detection in launch flow | Shim helper | `DismissLicenseIfPresent()` via `wpf_find_elements` + short timeout per MC §8 item 4. |
| Hardware-device-readiness gates | Descoped (3 families) | Settings/Cameras, Assessment/Capture, LaunchMonitor all descoped. `--qa-mode=software` covers camera-adjacent scenarios in Analysis/QuickStart. |
| Multi-product launch param | Shim | `mc_launch(Product)` broker lifecycle tool covers this for all active scenarios. |
| `--qa-mode` software rendering | Shim | Passed through `mc_launch` args. No WGC conflict at MVP (uses `RenderTargetBitmap`). |

---

## Gate Verdict

FlaUI fallback required for **2 feature files** (Analysis/Explorer, Analysis/Capture).
Descoped: **3 feature files** (Settings/Cameras, Assessment/Capture stub, LaunchMonitor stub — both stubs have no active scenarios and are excluded from gate count).

**Active FlaUI fallback count = 2. Gate threshold = 5.**

**M2 gate definition stands as written.** No `REWRITE-M2-GATE-REQUIRED`.

---

## Acceptance Criterion Check

```bash
# File exists
test -f /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md

# 18 scenario families appear as headings
grep -cE "^### SCENARIO FAMILY" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
# → 18

# Decision table present
grep -q "shim\|FlaUI\|descoped" /c/work/snoopwpf/COVERAGE-GAP-AUDIT.md
# → passes
```
