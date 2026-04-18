# Agent Recipes

Five end-to-end workflows for AI agents using the `wpf_*` MCP tools. Each recipe shows a
concrete debugging or automation task, the tool-call sequence, real example inputs, and the
kind of response to expect.

All examples assume the agent is connected to a running WPF app via one of the three modes
(NuGet co-located, injection, or brokered). See [Getting Started](getting-started.md) for
connection setup.

---

## Recipe 1 — Diagnose a silent data-binding failure

**Scenario:** A `TextBox` that should show a live measurement value is blank. The user sees no
error message. The binding was probably working at some point and broke silently after a
ViewModel refactor.

This recipe demonstrates snoopwpf's strongest differentiator: the ability to walk the full
binding chain and pinpoint exactly where data flow broke, something no UIA-based tool can do.

### Step 1 — Verify connection and check mutation state

```json
// Tool: wpf_get_session_info
// Parameters: (none)
```

<details>
<summary>Example response</summary>

```json
{
  "processName": "StrideAnalyzer",
  "pid": 9120,
  "dotnetVersion": "8.0.3",
  "mutationEnabled": false,
  "dispatchers": [
    {
      "id": 0,
      "threadId": 1,
      "windowNodeIds": ["0:1"]
    }
  ],
  "capabilities": ["tree", "properties", "diagnostics", "resources", "screenshots"]
}
```
</details>

`mutationEnabled: false` is fine for a read-only diagnostic session.

### Step 2 — Find the TextBox by name

```json
// Tool: wpf_find_elements
{
  "name": "StrideTextBox",
  "treeType": "visual"
}
```

<details>
<summary>Example response</summary>

```json
{
  "results": [
    {
      "node": {
        "nodeId": "0:88",
        "typeName": "System.Windows.Controls.TextBox",
        "name": "StrideTextBox",
        "displayName": "StrideTextBox",
        "childCount": 1,
        "hasBindingError": true,
        "depth": 5
      },
      "path": ["MainWindow", "Grid", "SessionPanel", "MetricsGrid", "StrideTextBox"]
    }
  ],
  "totalScanned": 312,
  "truncated": false
}
```
</details>

`hasBindingError: true` confirms there is a problem. Note the `nodeId` (`0:88`) for subsequent calls.

### Step 3 — Get a rich element summary

```json
// Tool: wpf_inspect_element
{
  "nodeId": "0:88"
}
```

<details>
<summary>Example response</summary>

```json
{
  "nodeId": "0:88",
  "typeName": "System.Windows.Controls.TextBox",
  "name": "StrideTextBox",
  "displayName": "StrideTextBox",
  "path": ["MainWindow", "Grid", "SessionPanel", "MetricsGrid", "StrideTextBox"],
  "parentNodeId": "0:71",
  "childCount": 1,
  "depth": 5,
  "dispatcherId": 0,
  "isVisible": true,
  "actualWidth": 120.0,
  "actualHeight": 28.0,
  "dataContextType": "StrideAnalyzer.ViewModel.SessionMetricsViewModel",
  "hasBindingErrors": true,
  "bindingErrorCount": 1,
  "triggerCount": null,
  "behaviorCount": null
}
```
</details>

The `dataContextType` is set (`SessionMetricsViewModel`) — the DataContext itself is not null.
The error is likely a path problem within the ViewModel.

### Step 4 — Inspect the binding on the Text property

```json
// Tool: wpf_get_binding_info
{
  "nodeId": "0:88",
  "propertyName": "Text"
}
```

<details>
<summary>Example response</summary>

```json
{
  "hasBinding": true,
  "bindingType": "Binding",
  "path": "CurrentStride.ValueMetric",
  "elementName": null,
  "relativeSource": null,
  "mode": "OneWay",
  "updateSourceTrigger": "Default",
  "converterTypeName": "StrideAnalyzer.Converters.MetricValueConverter",
  "sourceType": "StrideAnalyzer.ViewModel.SessionMetricsViewModel",
  "status": "PathError",
  "error": "Cannot find property 'ValueMetric' on type 'StrideAnalyzer.Models.StrideReading'",
  "dataContextIsNull": false,
  "dataContextType": "StrideAnalyzer.ViewModel.SessionMetricsViewModel",
  "resolvedValue": null,
  "childBindings": null
}
```
</details>

`status: "PathError"` and the error message give the exact diagnosis: the property was renamed
from `ValueMetric` to something else during a recent ViewModel refactor.

### Step 5 — Walk the binding chain for per-step values

```json
// Tool: wpf_resolve_binding
{
  "nodeId": "0:88",
  "propertyName": "Text"
}
```

<details>
<summary>Example response</summary>

```json
{
  "path": "CurrentStride.ValueMetric",
  "sourceTypeName": "StrideAnalyzer.ViewModel.SessionMetricsViewModel",
  "sourceValue": null,
  "pathSteps": [
    {
      "segment": "CurrentStride",
      "value": "StrideReading { StepLength=1.24, CadenceRpm=174 }"
    },
    {
      "segment": "ValueMetric",
      "value": null
    }
  ],
  "converterTypeName": "StrideAnalyzer.Converters.MetricValueConverter",
  "converterParameter": null,
  "mode": "OneWay",
  "validationErrors": [],
  "status": "PathError"
}
```
</details>

The walk shows that `CurrentStride` resolves successfully (there is a live `StrideReading`
object with real data), but `ValueMetric` does not exist on it. The correct property name is
visible from the printed object: it should be `StepLength` or `CadenceRpm`. The agent can now
report the exact rename needed in the XAML binding expression.

**Why this wins:** Snoopwpf is the only tool that resolves multi-step binding paths with
per-segment live values — pinpointing the broken segment without guesswork or adding debug
logging to the app.

---

## Recipe 2 — Toggle a feature flag safely via mutation

**Scenario:** An agent needs to enable a feature flag (`IsExportEnabled`) on a settings panel
to test export behavior. Mutation must be explicitly enabled, and the agent should verify the
result before proceeding.

### Step 1 — Verify mutation is enabled

```json
// Tool: wpf_get_session_info
// Parameters: (none)
// Key field: "mutationEnabled": true
```

`mutationEnabled: true` is required. If it reads `false`, mutation tools will return
`MUTATION_DISABLED` and the agent must ask the operator to restart the app with
`EnableMutation = true` in `SnoopAgentOptions`. (See [Appendix](#appendix-full-json-responses),
Recipe 2 Step 1 for the full response.)

### Step 2 — Find the feature toggle CheckBox

```json
// Tool: wpf_find_elements
{
  "name": "ExportFeatureToggle",
  "treeType": "visual"
}
// Returns: nodeId "0:204", typeName "System.Windows.Controls.CheckBox"
```

### Step 3 — Set the check state

```json
// [MUTATE] — requires EnableMutation=true in SnoopAgentOptions
// Tool: wpf_set_check_state
{
  "nodeId": "0:204",
  "state": "checked"
}
// If EnableMutation is false, the call returns instead:
// { "ok": false, "error": { "code": "MUTATION_DISABLED", "message": "..." } }
```

Success response: `{ "success": true, "stateChanged": true, "treeVersionDelta": 1, ... }`.
Disabled response: `{ "success": false, "failureReason": "MUTATION_DISABLED", ... }`.

### Step 4 — Wait for the property to confirm

```json
// Tool: wpf_wait_for_property
{
  "locator": "$name:ExportFeatureToggle",
  "propertyName": "IsChecked",
  "expectedValue": "True",
  "timeoutMs": 5000,
  "presenceExpected": "present"
}
// Returns: { "conditionMet": true, "actualValue": "True", "elapsedMs": 18, "pollCount": 1 }
```

### Step 5 — Capture a screenshot to confirm the UI state

```json
// Tool: wpf_capture_screenshot
{ "nodeId": "0:204" }
// Returns: { "blobRef": "blob:screenshot:0:204:f9a3c1b7", "sizeBytes": 3214, ... }
```

### Step 6 — Fetch the image

```json
// Tool: wpf_fetch_blob
{ "key": "blob:screenshot:0:204:f9a3c1b7" }
```

The response is a two-block MCP content response: block 0 is JSON metadata, block 1 is the
PNG `ImageContent`. The agent can pass the image to its vision capability to visually confirm
the checkmark is rendered.

**Why this wins:** `wpf_set_check_state` uses `SetCurrentValue`, which preserves any active
TwoWay binding on `IsChecked` — the bound `IsExportEnabled` property in the ViewModel is
updated through the normal binding pipeline, exactly as if the user clicked the checkbox.
No input simulation required.

---

## Recipe 3 — Find a virtualized list item

**Scenario:** A `ListBox` is bound to a collection of 800 session records. The agent needs to
select the record at index 500, which is far off-screen. Standard `wpf_find_elements` cannot
find it because WPF's `VirtualizingStackPanel` has not yet materialized the item container.

**Two tools handle virtualized selection.** `wpf_select_item_by_scroll(listNodeId, targetIndex)`
is preferred when you need explicit scroll-then-select behaviour (it forces realization by
scrolling to the target index before selecting); `wpf_select_item(nodeId, identifier="<index>")`
is preferred when you have a stable zero-based integer index and want lower-latency direct
selection via `BringIndexIntoView`. When you have the item text rather than an index, pass the
text string as `identifier` to `wpf_select_item` — it accepts exact text or an unambiguous
substring in addition to integer indices. This recipe uses the index path via `wpf_select_item`;
swap in `wpf_select_item_by_scroll` if your use-case requires the explicit scroll-first
guarantee (for example when targeting items near the scroll-budget boundary).

### Step 1 — Find the ListBox by type and automation ID

```json
// Tool: wpf_find_elements
{
  "typeName": "ListBox",
  "propertyConditions": [{ "property": "Name", "operator": "Equals", "value": "SessionHistoryList" }],
  "treeType": "visual"
}
// Returns: nodeId "0:55", childCount 12 (only 12 containers realized out of 800)
```

`childCount: 12` — only 12 item containers are realized even though the list has 800 items.
A direct `wpf_find_elements` search for an item at position 500 would fail.

### Step 2 — Attempt direct find (shows the limitation)

```json
// Tool: wpf_find_elements
{
  "typeName": "ListBoxItem",
  "propertyConditions": [{ "property": "Content", "operator": "Contains", "value": "Session #500" }],
  "rootNodeId": "0:55"
}
// Returns: { "results": [], "totalScanned": 12 }  — item not yet realized
```

### Step 3 — Select by index to trigger BringIndexIntoView

```json
// Tool: wpf_select_item
{ "nodeId": "0:55", "identifier": "500" }
// Returns: { "success": true, "stateChanged": true, "treeVersionDelta": 14 }
```

`treeVersionDelta: 14` reflects the burst of new item containers added to the tree as WPF
scrolled the panel to bring index 500 into view.

### Step 4 — Poll for structural changes and then find the item

```json
// Tool: wpf_pump_until_idle
{ "timeoutMs": 3000 }
// Returns: { "idledAfterMs": 84, "resourcesPolled": ["Dispatcher", "CompositionRendering"] }
```

```json
// Tool: wpf_find_elements
{
  "typeName": "ListBoxItem",
  "propertyConditions": [{ "property": "IsSelected", "operator": "Equals", "value": "True" }],
  "rootNodeId": "0:55"
}
// Returns: nodeId "0:621", displayName "Session #500 — 2026-03-14" (now materialized)
```

The item is now materialized and can be inspected with `wpf_inspect_element` or
`wpf_get_binding_info`.

**Why this wins:** `wpf_select_item` with an integer index calls `BringIndexIntoView` on the
virtualized panel, triggering WPF's own item realization path — the only reliable way to
materialize a specific item in a large virtualized list without scrolling the UI manually.

---

## Recipe 4 — Audit a style trigger chain

**Scenario:** A Button has a hover color that looks wrong — too dark when the mouse is over it.
The designer's intent is `#FF0078D4` (blue) for hover but the rendered color is `#FF5C2D91`
(purple). The agent needs to find where the override comes from.

### Step 1 — Find the Button

```json
// Tool: wpf_find_elements
{ "name": "PrimaryActionButton", "treeType": "visual" }
// Returns: nodeId "0:180", typeName "System.Windows.Controls.Button"
```

### Step 2 — Inspect the element for trigger and behavior counts

```json
// Tool: wpf_inspect_element
{ "nodeId": "0:180" }
// Key fields: triggerCount: null, behaviorCount: null
```

`triggerCount: null` means the count has not been evaluated yet. Call `wpf_get_triggers` to
fetch them.

### Step 3 — Enumerate all triggers

```json
// Tool: wpf_get_triggers
{ "nodeId": "0:180" }
```

Returns an array of trigger objects. The relevant one:
```json
{
  "triggerType": "Trigger",
  "source": "ControlTemplate",
  "conditions": [{ "property": "IsMouseOver", "value": "True" }],
  "setters": [{ "property": "Background", "value": "{DynamicResource HoverBackgroundBrushKey}" }]
}
```

The hover trigger sets `Background` via `{DynamicResource HoverBackgroundBrushKey}`. The
resource key is the suspected location of the conflict.

### Step 4 — Look up resources with the conflicting key

```json
// Tool: wpf_get_resources
{ "nodeId": "0:180", "resourceKey": "HoverBackgroundBrushKey" }
```

Returns two entries — closest scope wins:
```json
{ "key": "HoverBackgroundBrushKey", "valueSummary": "#FF5C2D91", "origin": "Local",    "dictionarySource": "ActionBar" },
{ "key": "HoverBackgroundBrushKey", "valueSummary": "#FF0078D4", "origin": "Shadowed", "dictionarySource": "App" }
```

The resource lookup shows two entries for `HoverBackgroundBrushKey`. The closest scope wins:
the `ActionBar` control's local `ResourceDictionary` defines `#FF5C2D91` (purple), which
shadows the application-level definition of `#FF0078D4` (blue). The agent can now report that
the override was added to `ActionBar`'s resource dictionary — probably unintentionally during
a theme change.

**Why this wins:** `wpf_get_resources` exposes the full resource precedence chain including
shadowed entries, ordered closest-scope-first — making it possible to identify unintentional
resource overrides that are invisible in normal XAML editing.

---

## Recipe 5 — Capture a screenshot without LLM context bloat

**Scenario:** An agent wants to capture the current state of a form as a PNG image to pass to
its vision capability, without embedding a large base64 payload in every tool response.

Snoopwpf uses a two-step blob protocol: `wpf_capture_screenshot` returns only a lightweight
reference token (`blobRef`), and `wpf_fetch_blob` delivers the actual binary content separately.
This keeps JSON responses under ~2 KB regardless of image size.

### Step 1 — Find the form panel to capture

```json
// Tool: wpf_find_elements
{ "name": "SessionSummaryForm", "treeType": "visual" }
// Returns: nodeId "0:95", typeName "System.Windows.Controls.Grid"
```

### Step 2 — Capture the screenshot (step 1 of 2: get the reference token)

```json
// Tool: wpf_capture_screenshot
{ "nodeId": "0:95" }
// Returns: { "blobRef": "blob:screenshot:0:95:c3e7a912", "sizeBytes": 52840, "width": 640, "height": 480 }
```

The response is a small JSON object (~150 bytes). The 52 KB PNG is held in the in-process
blob store under the key `blob:screenshot:0:95:c3e7a912`. The LLM's context window is not
expanded at this point.

**Blob lifetime:** Blobs expire after 60 seconds by default (configurable via
`SnoopAgentOptions.BlobTtl`). Re-run `wpf_capture_screenshot` if the key has expired.

### Step 3 — Fetch the image (step 2 of 2: retrieve the binary)

```json
// Tool: wpf_fetch_blob
{ "key": "blob:screenshot:0:95:c3e7a912" }
```

The response is a two-block MCP content response: **Block 0** is JSON metadata
(`key`, `mimeType`, `sizeBytes`); **Block 1** is an MCP `ImageContent` block with
`mediaType: "image/png"`. The MCP client passes block 1 to the model's vision capability as a
native image, not as base64 embedded in a JSON string.

### Why the two-step protocol matters

In a single-step design, `wpf_capture_screenshot` would return a base64-encoded PNG inline.
A 640×480 screenshot at typical WPF DPI produces roughly 50–100 KB of PNG which encodes to
~70–135 KB of base64 text. Embedded in a JSON tool response, this consumes tokens on every
tool call that returns it — even when the agent only needed the dimensions to verify size.

The two-step protocol keeps **every JSON response under 2 KB**. The image bytes are delivered
only when `wpf_fetch_blob` is called, and they are delivered as a native MCP `ImageContent`
block rather than a JSON string — so the model's vision capability receives a proper image
object rather than parsing base64 out of a JSON field.

This pattern extends to any large binary payload the agent might generate. The same blob-store
mechanism is available to future tools that produce large outputs (e.g. diagnostic snapshots,
serialized trees).

**Why this wins:** The blob-ref protocol eliminates screenshot base64 from JSON tool responses,
keeping token costs predictable and allowing high-frequency capture without context-window
pressure.

---

## Quick reference — tool call order patterns

| Goal | Tool sequence |
|---|---|
| Find an element by name | `wpf_find_elements(name=...)` → `wpf_inspect_element(nodeId)` |
| Diagnose blank/wrong value | `wpf_find_elements` → `wpf_get_binding_info` → `wpf_resolve_binding` |
| Mutate a property safely | `wpf_get_session_info` (check `mutationEnabled`) → mutation tool → `wpf_wait_for_property` |
| Select virtualized list item (by index) | `wpf_find_elements` (get list nodeId) → `wpf_select_item(identifier="N")` → `wpf_pump_until_idle` → `wpf_find_elements` |
| Select virtualized list item (scroll-first) | `wpf_find_elements` (get list nodeId) → `wpf_select_item_by_scroll(nodeId, targetIndex)` → `wpf_find_elements` |
| Audit style / resource conflict | `wpf_inspect_element` → `wpf_get_triggers` → `wpf_get_resources(resourceKey=...)` |
| Capture form state | `wpf_capture_screenshot(nodeId)` → `wpf_fetch_blob(blobRef)` |

---

## Appendix — Full JSON responses

Expanded tool response examples for recipes where the recipe body shows only key fields.

### Recipe 2 — Step 1: wpf_get_session_info (mutation-enabled session)

```json
{
  "processName": "StrideAnalyzer",
  "pid": 9120,
  "dotnetVersion": "8.0.3",
  "mutationEnabled": true,
  "dispatchers": [{ "id": 0, "threadId": 1, "windowNodeIds": ["0:1"] }],
  "capabilities": ["tree", "properties", "diagnostics", "resources", "screenshots"]
}
```

### Recipe 2 — Step 2: wpf_find_elements (ExportFeatureToggle)

```json
{
  "results": [
    {
      "node": {
        "nodeId": "0:204",
        "typeName": "System.Windows.Controls.CheckBox",
        "name": "ExportFeatureToggle",
        "displayName": "ExportFeatureToggle",
        "childCount": 1,
        "hasBindingError": false,
        "depth": 6
      },
      "path": ["MainWindow", "Grid", "SettingsPanel", "FeaturesGroup", "ExportSection", "ExportFeatureToggle"]
    }
  ],
  "totalScanned": 312,
  "truncated": false
}
```

### Recipe 4 — Step 2: wpf_inspect_element (PrimaryActionButton)

```json
{
  "nodeId": "0:180",
  "typeName": "System.Windows.Controls.Button",
  "name": "PrimaryActionButton",
  "displayName": "PrimaryActionButton",
  "path": ["MainWindow", "Grid", "ActionBar", "PrimaryActionButton"],
  "parentNodeId": "0:150",
  "childCount": 1,
  "depth": 4,
  "dispatcherId": 0,
  "isVisible": true,
  "actualWidth": 140.0,
  "actualHeight": 36.0,
  "dataContextType": "StrideAnalyzer.ViewModel.MainViewModel",
  "hasBindingErrors": false,
  "bindingErrorCount": 0,
  "triggerCount": null,
  "behaviorCount": null
}
```

### Recipe 4 — Step 3: wpf_get_triggers (full trigger array)

```json
[
  {
    "triggerType": "Trigger",
    "isActive": false,
    "source": "ControlTemplate",
    "conditions": [{ "property": "IsMouseOver", "value": "True" }],
    "setters": [{ "property": "Background", "value": "{DynamicResource HoverBackgroundBrushKey}" }]
  },
  {
    "triggerType": "Trigger",
    "isActive": false,
    "source": "ControlTemplate",
    "conditions": [{ "property": "IsPressed", "value": "True" }],
    "setters": [{ "property": "Background", "value": "{DynamicResource PressedBackgroundBrushKey}" }]
  }
]
```

### Recipe 4 — Step 4: wpf_get_resources (shadow revealed)

```json
{
  "items": [
    { "key": "HoverBackgroundBrushKey", "valueTypeName": "System.Windows.Media.SolidColorBrush",
      "valueSummary": "#FF5C2D91", "origin": "Local",    "dictionarySource": "ActionBar" },
    { "key": "HoverBackgroundBrushKey", "valueTypeName": "System.Windows.Media.SolidColorBrush",
      "valueSummary": "#FF0078D4", "origin": "Shadowed", "dictionarySource": "App" }
  ],
  "nextCursor": null,
  "totalCount": 2,
  "hasMore": false
}
```
