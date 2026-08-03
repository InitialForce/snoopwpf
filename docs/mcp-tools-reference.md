# MCP Tools Reference

All 32 tools exposed by SnoopWPF.Agent. Tool names are prefixed with `wpf_`.

Error responses follow a common schema — see [Error Codes](#error-codes) at the bottom.

---

## wpf_diagnostics

Self-health snapshot of the running agent, with the inspected process's session info
folded into the `sessionInfo` field.

**Parameters:** none

**Returns:**

```json
{
  "agentVersion": "6.2.0",
  "mode": "Brokered",
  "dispatcherHealthy": true,
  "dispatcherQueueLength": 0,
  "blobStoreCount": 0,
  "blobStoreBytes": 0,
  "auditLogDepth": 0,
  "sessionPolicy": {
    "enableMutation": false,
    "enableAutomation": false,
    "allowSensitiveRetention": false
  },
  "uptimeSeconds": 12.4,
  "sessionInfo": {
    "processName": "MyApp",
    "pid": 12345,
    "dotnetVersion": "8.0.3",
    "mutationEnabled": false,
    "dispatchers": [
      {
        "id": 0,
        "threadId": 1,
        "windowNodeIds": ["0:1", "0:2"]
      }
    ],
    "capabilities": ["tree", "properties", "diagnostics", "resources", "screenshots"],
    "windows": [
      { "nodeId": "0:1", "title": "MyApp", "width": 1280, "height": 720, "locator": "$type:MainWindow" }
    ]
  }
}
```

`sessionInfo` is produced by the same Dispatcher round-trip that sets `dispatcherHealthy`;
it is `null` when the probe fails (`dispatcherHealthy: false`).

**Usage:** Call this first on every new session to verify the agent is functional and to get
the window node IDs (from `sessionInfo.dispatchers[].windowNodeIds` or `sessionInfo.windows`)
for subsequent calls.

---

## wpf_get_windows

List the top-level WPF windows in the target process.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `includeHidden` | boolean | `false` | Include windows with `Visibility != Visible`. |

**Returns:**

```json
[
  {
    "nodeId": "0:1",
    "title": "Main Window",
    "typeName": "MyApp.MainWindow",
    "width": 1200.0,
    "height": 800.0,
    "dispatcherId": 0
  }
]
```

**Usage:** Start here to get node IDs for the windows you want to inspect.

---

## wpf_get_visual_tree

Get a depth-limited snapshot of the visual tree.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `rootNodeId` | string | *(app root)* | Start from this node. Omit for entire tree. |
| `maxDepth` | integer | `3` | Maximum depth (max 10). |
| `treeType` | string | `"visual"` | Tree to inspect: `"visual"`, `"logical"`, or `"automation"`. |
| `includeProperties` | string[] | *(none)* | DependencyProperty names to include inline on each node (max 10). |

**Returns:**

```json
{
  "root": {
    "nodeId": "0:1",
    "typeName": "MyApp.MainWindow",
    "name": "",
    "displayName": "MyApp.MainWindow",
    "childCount": 1,
    "hasBindingError": false,
    "depth": 0,
    "childrenTruncated": false,
    "properties": null,
    "children": [
      {
        "nodeId": "0:5",
        "typeName": "System.Windows.Controls.Grid",
        "name": "rootGrid",
        "displayName": "rootGrid",
        "childCount": 3,
        "hasBindingError": false,
        "depth": 1,
        "childrenTruncated": false,
        "children": []
      }
    ]
  },
  "truncated": false,
  "returnedNodeCount": 47
}
```

`childrenTruncated: true` on a node means it has children that were cut by `maxDepth`
or the 5000-node hard cap. Use `wpf_get_children` to retrieve them.

**Inline properties example:**

```json
{
  "rootNodeId": "0:5",
  "maxDepth": 2,
  "includeProperties": ["Background", "Visibility"]
}
```

Each node's `properties` field is then populated with the named values (or
`"[REDACTED]"` for sensitive properties).

---

## wpf_get_children

Get cursor-paginated direct children of a node.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(app roots)* | Parent node ID. Omit for top-level windows. |
| `treeType` | string | `"visual"` | `"visual"`, `"logical"`, or `"automation"`. |
| `cursor` | string | *(none)* | Pagination cursor from previous response. |
| `take` | integer | `50` | Items per page (max 200). |

**Returns:**

```json
{
  "items": [
    {
      "nodeId": "0:10",
      "typeName": "System.Windows.Controls.Button",
      "name": "okButton",
      "displayName": "okButton",
      "childCount": 1,
      "hasBindingError": false,
      "depth": 2,
      "childrenTruncated": false
    }
  ],
  "nextCursor": "snap_a1b2_50",
  "totalCount": 150,
  "hasMore": true,
  "stale": false
}
```

`stale: true` means the snapshot used for pagination expired (30-second TTL). The
response contains a fresh first page — discard your cursor and restart pagination.

---

## wpf_get_ancestors

Get the ancestor chain from a node up to the root.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node to start from. |
| `maxLevels` | integer | *(all)* | Maximum number of ancestors to return. |

**Returns:**

```json
{
  "ancestors": [
    {
      "nodeId": "0:5",
      "typeName": "System.Windows.Controls.Grid",
      "name": "rootGrid",
      "dataContextType": "MyApp.ViewModel.MainViewModel"
    },
    {
      "nodeId": "0:1",
      "typeName": "MyApp.MainWindow",
      "name": "",
      "dataContextType": "MyApp.ViewModel.MainViewModel"
    }
  ]
}
```

Ordered: immediate parent first, root last.

---

## wpf_find_elements

Search the visual tree for elements matching criteria.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `typeName` | string | *(none)* | Type name substring (case-insensitive). E.g. `"Button"`. |
| `name` | string | *(none)* | Exact `x:Name` match (case-sensitive). |
| `rootNodeId` | string | *(whole tree)* | Scope the search to a subtree. |
| `propertyConditions` | object[] | *(none)* | Filter by property values. See below. |
| `treeType` | string | `"visual"` | `"visual"`, `"logical"`, or `"automation"`. |
| `maxResults` | integer | `50` | Maximum results (max 100). |

`propertyConditions` format:

```json
[
  { "property": "IsEnabled", "operator": "Equals", "value": "False" },
  { "property": "Text", "operator": "Contains", "value": "error" }
]
```

Operators: `"Equals"` or `"Contains"` (case-insensitive).

**Returns:**

```json
{
  "results": [
    {
      "node": { "nodeId": "0:42", "typeName": "System.Windows.Controls.Button", ... },
      "path": ["MainWindow", "Grid", "StackPanel", "Button"]
    }
  ],
  "totalScanned": 500,
  "truncated": false
}
```

---

## wpf_inspect_element

Get a rich summary of a single element.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID to inspect. |

**Returns:**

```json
{
  "nodeId": "0:42",
  "typeName": "System.Windows.Controls.Button",
  "name": "submitButton",
  "displayName": "submitButton",
  "path": ["MainWindow", "Grid", "StackPanel", "Button"],
  "parentNodeId": "0:30",
  "childCount": 1,
  "depth": 3,
  "dispatcherId": 0,
  "isVisible": true,
  "actualWidth": 120.0,
  "actualHeight": 32.0,
  "dataContextType": "MyApp.ViewModel.MainViewModel",
  "hasBindingErrors": true,
  "bindingErrorCount": 1,
  "triggerCount": null,
  "behaviorCount": null
}
```

`triggerCount` and `behaviorCount` are `null` (not evaluated). Call
`wpf_get_triggers` and `wpf_get_behaviors` to retrieve them.

---

## wpf_get_properties

Get cursor-paginated properties of an element with values and binding status.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node to inspect. |
| `filter` | string | *(none)* | Case-insensitive substring filter on property name. |
| `category` | string | `"all"` | Filter by category: `"layout"`, `"color"`, `"font"`, `"grid"`, `"all"`. |
| `includeDefaults` | boolean | `false` | Include properties at their default value. |
| `cursor` | string | *(none)* | Pagination cursor. |
| `take` | integer | `100` | Items per page (max 200). |

**Returns:**

```json
{
  "items": [
    {
      "name": "Background",
      "typeName": "System.Windows.Media.Brush",
      "value": "#FF0078D4",
      "valueSource": "Local",
      "isLocallySet": true,
      "isDataBound": false,
      "hasBindingError": false,
      "bindingError": null,
      "isReadOnly": false,
      "hasTypeConverter": true,
      "isRedacted": false
    },
    {
      "name": "Password",
      "typeName": "System.String",
      "value": "[REDACTED]",
      "valueSource": "Local",
      "isLocallySet": true,
      "isDataBound": false,
      "hasBindingError": false,
      "bindingError": null,
      "isReadOnly": false,
      "hasTypeConverter": true,
      "isRedacted": true
    }
  ],
  "nextCursor": null,
  "totalCount": 80,
  "hasMore": false,
  "stale": false
}
```

Properties are sorted by name for stable pagination across pages.

---

## wpf_set_property

Set a property value on an element. **Disabled by default** — requires
`EnableMutation = true` in `SnoopAgentOptions`.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Target node. |
| `propertyName` | string | *(required)* | Property to set. |
| `value` | string | *(required)* | New value as a string. See format notes below. |

**Value format notes:**

| Type | Format |
|------|--------|
| `Color` | `"#RRGGBB"`, `"#AARRGGBB"`, or named color (`"Red"`, `"Transparent"`) |
| `Thickness` | Single value (`"4"`) or four values (`"4,8,4,8"`) — L,T,R,B |
| `GridLength` | `"Auto"`, `"*"`, `"2*"`, or pixel count (`"100"`) |
| `Visibility` | `"Visible"`, `"Hidden"`, `"Collapsed"` |
| `bool` | `"True"` or `"False"` |
| Enums | Enum member name (e.g. `"Center"` for `HorizontalAlignment`) |

All values parsed with `InvariantCulture`. A decimal point is `.` regardless of
system locale.

**Returns:**

```json
{
  "success": true,
  "previousValue": "#FFFFFFFF",
  "newValue": "#FF0078D4",
  "error": null
}
```

On failure:

```json
{
  "success": false,
  "previousValue": "#FFFFFFFF",
  "newValue": null,
  "error": "MutationDisabled"
}
```

---

## wpf_run_diagnostics

Run Snoop's built-in diagnostic providers on the visual tree.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(whole tree)* | Scope to a subtree. |
| `providers` | string[] | *(all)* | Specific provider names to run. |
| `minLevel` | string | *(all)* | Minimum severity: `"Info"`, `"Warning"`, `"Error"`, `"Critical"`. |
| `cursor` | string | *(none)* | Pagination cursor. |
| `take` | integer | `50` | Items per page (max 200). |

**Returns:**

```json
{
  "items": [
    {
      "name": "BindingError",
      "description": "Binding error on TextBlock.Text: property path 'NonExistentProp' not found",
      "area": "Bindings",
      "level": "Error",
      "nodeId": "0:55",
      "nodePath": ["MainWindow", "Grid", "TextBlock"]
    },
    {
      "name": "NonVirtualizedList",
      "description": "ListBox contains 500 items but ItemsPanel is not a VirtualizingStackPanel",
      "area": "Performance",
      "level": "Warning",
      "nodeId": "0:70",
      "nodePath": ["MainWindow", "Grid", "ListBox"]
    }
  ],
  "nextCursor": null,
  "totalCount": 2,
  "hasMore": false
}
```

Results sorted by severity: Critical first, then Error, Warning, Info.

---

## wpf_get_resources

Get resources from the resource dictionary hierarchy with precedence ordering.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(app root)* | Start resource lookup from this element. |
| `resourceKey` | string | *(none)* | Filter by resource key (exact match). |
| `cursor` | string | *(none)* | Pagination cursor. |
| `take` | integer | `50` | Items per page (max 200). |

**Returns:**

```json
{
  "items": [
    {
      "key": "PrimaryColor",
      "valueTypeName": "System.Windows.Media.SolidColorBrush",
      "valueSummary": "#FF0078D4",
      "origin": "Local",
      "dictionarySource": "MainWindow"
    },
    {
      "key": "PrimaryColor",
      "valueTypeName": "System.Windows.Media.SolidColorBrush",
      "valueSummary": "#FF5C2D91",
      "origin": "Shadowed",
      "dictionarySource": "App"
    }
  ],
  "nextCursor": null,
  "totalCount": 2,
  "hasMore": false
}
```

Closest scope first. Shadowed resources (overridden by a closer dictionary) are
included with `origin: "Shadowed"`.

---

## wpf_capture_screenshot

Capture a PNG screenshot of an element or window.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(first visible window)* | Element or window to capture. |

**Returns:** A single JSON text block containing metadata and a `blobRef` key:

```json
{
  "width": 800,
  "height": 600,
  "nodeId": "0:1",
  "blobRef": "blob:screenshot:0:1:a1b2c3d4",
  "sizeBytes": 45678,
  "mimeType": "image/png"
}
```

The PNG bytes are stored in the in-process BlobStore under the `blobRef` key.
Pass the key to `wpf_fetch_blob` to retrieve the actual image bytes.
Blobs expire after the session-configured TTL (default 60 seconds); re-run the tool to get a fresh reference.

**Fallback order:** If `nodeId` is not provided, the tool captures the main window.
If the main window is not visible, it falls back to the first visible window.

**Errors:** Returns `ELEMENT_NOT_RENDERABLE` for zero-size or fully hidden elements.

---

## wpf_get_triggers

Get all triggers on an element (Style, ControlTemplate, DataTemplate, and direct).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node to inspect. |

**Returns:**

```json
[
  {
    "triggerType": "DataTrigger",
    "isActive": true,
    "source": "Style",
    "conditions": [
      { "property": "IsEnabled", "value": "False" }
    ],
    "setters": [
      { "property": "Opacity", "value": "0.5" }
    ]
  }
]
```

`source` values: `"Style"`, `"ControlTemplate"`, `"DataTemplate"`, `"Element"`.

---

## wpf_get_behaviors

Get all Blend behaviors and actions attached to an element.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node to inspect. |

**Returns:**

```json
[
  {
    "typeName": "EventTriggerBehavior",
    "assemblyName": "Microsoft.Xaml.Behaviors",
    "properties": [
      { "name": "EventName", "value": "Click" },
      { "name": "SourceObject", "value": "null" }
    ]
  }
]
```

Works with both `System.Windows.Interactivity` (legacy Blend SDK) and
`Microsoft.Xaml.Behaviors.Wpf` (modern package).

---

## wpf_click

Invoke the primary click action on a WPF element via the UI Automation InvokePattern (L1).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element to click. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Prefer `wpf_execute_command` (L0) over `wpf_click` whenever the element has a
`Command` binding (`ButtonBase.CommandProperty` is non-null). Use `wpf_get_properties` to check
for a Command binding before calling this tool. When `wpf_click` succeeds on a command-bound
element the response includes a `wpf_execute_command` suggestion — use that tool instead on
the next interaction. Automation must be enabled (`EnableAutomation = true` in `SnoopAgentOptions`).

**Limitations:** Requires the element to expose the `IInvokeProvider` automation pattern.
Controls that do not support `IInvokeProvider` (e.g. plain TextBlock, Image) will fail with
`PatternNotSupported`. Does not simulate mouse movement or hover events. Does not check
`IsEnabled` or `IsVisible` before invoking — verify actionability with `wpf_inspect_element`
first if the element may be disabled.

**Applies to:** Button, RepeatButton, ToggleButton, RadioButton, CheckBox, MenuItem, Hyperlink,
and any UIElement whose AutomationPeer supports `IInvokeProvider`.

---

## wpf_execute_command

Execute the `ICommand` bound to a WPF element (e.g. `Button.Command`). Operates at tier L0 —
uses the WPF command system directly, no raw Win32 input.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element whose Command should be executed. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 2,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Always prefer `wpf_execute_command` over `wpf_click` when a Command is bound
to the element. Use `wpf_get_properties` to confirm the element has a non-null Command before
calling. Mutation must be enabled (`EnableMutation = true` in `SnoopAgentOptions`).

**Limitations:** Only resolves the `ButtonBase.CommandProperty` dependency property; custom
command properties on non-`ButtonBase` elements are not supported by this tool. `CanExecute`
is checked before `Execute` — if it returns `false` the call fails with `CannotExecuteCommand`.
`CommandParameter` is forwarded automatically from `ButtonBase.CommandParameterProperty`.

**Applies to:** Button, RepeatButton, ToggleButton, RadioButton, CheckBox, MenuItem, Hyperlink,
and any other `ButtonBase`-derived control with a Command binding.

---

## wpf_expand_collapse

Expand or collapse a WPF element via the UI Automation ExpandCollapsePattern (L1).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element to expand or collapse. |
| `action` | string | *(required)* | Action to perform: `"expand"` or `"collapse"`. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 5,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** The action is idempotent — expanding an already-expanded element succeeds
without error. Automation must be enabled (`EnableAutomation = true` in `SnoopAgentOptions`).
After expanding a `TreeViewItem`, call `wpf_get_children` to retrieve the newly revealed child
nodes.

**Limitations:** Elements that do not expose `IExpandCollapseProvider` (e.g. plain Button,
TextBox) are rejected with `PatternNotSupported`. Virtualized tree nodes may not be in the
visual tree; scroll or realise them first. Does not check `IsEnabled` or `IsVisible` before
acting.

**Applies to:** TreeViewItem, Expander, GroupItem (CollectionViewSource groups), and any
UIElement whose AutomationPeer supports `IExpandCollapseProvider`.

---

## wpf_select_item

Select an item in a `ListBox`, `ComboBox`, or any `Selector` control. A single discriminated
tool with three selection modes — pass **exactly one** of `identifier` or `index`.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the ItemsControl/Selector whose selection should be changed. |
| `identifier` | string | `null` | Identifier-mode selector: zero-based integer index (`"0"`), exact item text, or unambiguous substring of item text. Pass this OR `index`. |
| `index` | int | `null` | Index-mode selector: exact zero-based index. Pass this OR `identifier`. |
| `scrollToRealize` | bool | `false` | Index mode only: scroll the list to materialize the container at `index` before selecting. Ignored in identifier mode. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Selection mode is chosen by which parameter you pass:
1. **`identifier`** — resolves a zero-based index string (`"0"`), exact item text
   (case-insensitive `ToString()` match), or an unambiguous substring. Ambiguous substrings fail
   with `LOCATOR_AMBIGUOUS`. This mode auto-realizes a virtualized item when it resolves a match.
2. **`index`** (with `scrollToRealize` = `false`, the default) — selects by exact zero-based index
   without forcing container realization; use for non-virtualized or already-realized items.
3. **`index` with `scrollToRealize` = `true`** — scrolls a virtualized list
   (`VirtualizingStackPanel`) until the container at `index` is materialized, then selects it.

Passing neither or both of `identifier`/`index` fails with `INVALID_ARGUMENT`.

Mutation must be enabled (`EnableMutation = true` in `SnoopAgentOptions`). Operates at L0 —
uses `DependencyObject.SetCurrentValue` on the dependency property, so existing TwoWay bindings
and triggers remain intact; no raw Win32 input.

**Limitations:** Multi-selection controls (`ListBox` with `SelectionMode=Multiple`) will have
their selection replaced (not appended) by this tool.

**Applies to:** ListBox, ListView, ComboBox, and any `Selector` subclass.

---

## wpf_set_text_value

Set the text content of a `TextBox`, `PasswordBox`, or `RichTextBox` via `SetCurrentValue` on the
text dependency property (L0). No raw Win32 input is used.
Uses `DependencyObject.SetCurrentValue` so existing TwoWay bindings and triggers remain intact — setting a value does NOT clear the binding chain.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element whose text should be set. |
| `value` | string | *(required)* | The new text value to assign. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Use this tool instead of simulated keystrokes whenever the target is a
text-input control. For `PasswordBox`, the value is treated as sensitive (`S3`) and is redacted
from all log output; `previousValue` in the response is always `"[REDACTED]"`. Mutation must
be enabled (`EnableMutation = true` in `SnoopAgentOptions`). For `RichTextBox`, this tool
replaces the entire flow document with a single paragraph containing the supplied plain text;
existing formatting is discarded.

**Limitations:** Does not support multi-paragraph rich text or inline formatting. For
`PasswordBox`, `Password.SecurePassword` is not accessible from the agent layer. If the `Text`
property has a two-way binding, the bound source will be updated via the normal DP change
notification path.

**Applies to:** TextBox, PasswordBox, RichTextBox.

---

## wpf_set_check_state

Set the checked state of a `CheckBox` or `RadioButton` via `SetCurrentValue` on
`ToggleButton.IsCheckedProperty` (L0). No raw Win32 input is used.
Uses `DependencyObject.SetCurrentValue` so existing TwoWay bindings and triggers remain intact — setting a value does NOT clear the binding chain.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element whose check state should be set. |
| `state` | string | *(required)* | Target check state: `"checked"`, `"unchecked"`, or `"indeterminate"`. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Use this tool instead of simulated clicks whenever the target is a `CheckBox`
or `RadioButton` and you need a deterministic final state. For `CheckBox`, all three states
are supported; `"indeterminate"` requires `IsThreeState = true` on the `CheckBox`. For
`RadioButton`, only `"checked"` is meaningful — programmatic unchecking from outside the group
is not supported by WPF. Mutation must be enabled (`EnableMutation = true` in
`SnoopAgentOptions`).

**Limitations:** Bare `ToggleButton` (not `CheckBox` or `RadioButton`) is rejected with
`PatternNotSupported` — use `wpf_toggle` instead.

**Applies to:** CheckBox, RadioButton.

---

## wpf_set_slider_value

Sets the `Value` of a `Slider` or any `RangeBase` element via `SetCurrentValue` on
`RangeBase.ValueProperty` (L0). No raw Win32 input is used.
Uses `DependencyObject.SetCurrentValue` so existing TwoWay bindings and triggers remain intact — setting a value does NOT clear the binding chain.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the Slider element whose value should be set. |
| `value` | number | *(required)* | Target value. Clamped to `Slider.Minimum..Maximum` by WPF unless `normalized=true`. |
| `normalized` | boolean | `false` | When `false`, value is an absolute number. When `true`, value is a fraction in `[0.0, 1.0]` mapped to `Minimum..Maximum`. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Use this tool instead of simulated mouse drags whenever the target is a Slider and
you need a deterministic final value. Pass `normalized=false` (the default) to supply an absolute
value; WPF will clamp it to `[Minimum, Maximum]` automatically. Pass `normalized=true` to supply
a fraction in `[0.0, 1.0]` — the engine maps it to `Minimum + value × (Maximum − Minimum)`.
Mutation must be enabled (`EnableMutation = true` in `SnoopAgentOptions`).

**Limitations:** The tool targets `RangeBase.ValueProperty` only; `TickFrequency` and
`IsSnapToTickEnabled` are respected by WPF's own coerce logic, so the final stored value may
differ from the requested value when tick-snapping is active. If the `Value` property has a
two-way binding, the bound source will be updated via the normal DP change notification path.

**Applies to:** Slider, ProgressBar, ScrollBar, and any other `RangeBase` subclass.

---

## wpf_toggle

Flip the toggle state of a WPF element via the UI Automation TogglePattern (L1).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element to toggle. |

**Returns:**

```json
{
  "success": true,
  "stateChanged": true,
  "treeVersionDelta": 1,
  "failureReason": null,
  "suggestion": null
}
```

**Guidelines:** Use `wpf_toggle` when the target state is unknown and you simply want to flip
the current `IsChecked` state. When a specific final state (checked/unchecked/indeterminate) is
required, prefer `wpf_set_check_state` (L0) over `wpf_toggle` — `wpf_toggle` is
non-deterministic. Automation must be enabled (`EnableAutomation = true` in `SnoopAgentOptions`).

**Limitations:** `CheckBox` and `RadioButton` are rejected with `PatternNotSupported` — use
the deterministic L0 tool `wpf_set_check_state` for those controls. Two successive calls
return the element to its original state.

**Applies to:** ToggleButton (bare, not CheckBox or RadioButton), MenuItem with
`IsCheckable = true`, and any UIElement whose AutomationPeer supports `IToggleProvider`.

---

## wpf_poll_changes

Non-blocking structural-change detection — returns a changeset of added/removed node IDs and
the current `treeVersion` to use as the baseline for the next call.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `sinceVersion` | integer (long) | `0` | Tree version from a previous call. Pass `0` on the first call to receive all currently registered nodes as `"added"`. |
| `rootLocator` | string | *(none)* | Optional WpfLocator string to scope the poll to a subtree (e.g. `"$type:MainWindow"`). Omit for the full application tree. |

**Returns:**

```json
{
  "added": ["0:55", "0:56"],
  "removed": ["0:40"],
  "treeVersion": 12,
  "scannedNodeCount": 320
}
```

**Usage pattern:**
1. Call with `sinceVersion=0` to get the initial `treeVersion`.
2. Perform mutations (`wpf_set_property`, `wpf_execute_command`, etc.).
3. Call again with the previously returned `treeVersion` to receive the delta.

**Guidelines:** This tool does not wait for mutations to settle. Use `wpf_pump_until_idle`
before polling when you need deterministic results after a UI-triggered async operation.

**Limitations:** Hard cap of 5000 nodes per poll to protect against unbounded traversal.
Change kinds: `"added"` (node appeared after `sinceVersion`) and `"removed"` (node was present
at `sinceVersion` but is no longer in the tree).

**Applies to:** Full application visual tree, or any subtree scoped via `rootLocator`.

---

## wpf_pump_until_idle

Wait until the WPF Dispatcher queue AND composition rendering pipeline are simultaneously idle
(AND-gate). Returns immediately when idle; throws `DISPATCHER_BUSY` if the 5-second
animation-runaway ceiling is reached.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `timeoutMs` | integer | `5000` | Maximum wait time in milliseconds. Capped at 5000. |
| `resources` | string[] | *(all)* | Optional array of resource names to monitor (e.g. `["Dispatcher", "CompositionRendering"]`). Omit or `null` to monitor all built-in resources. |

**Returns:**

```json
{
  "idledAfterMs": 142,
  "resourcesPolled": ["Dispatcher", "CompositionRendering"]
}
```

**Guidelines:** Use before `wpf_poll_changes` or `wpf_wait_for_property` when you need
deterministic results after a UI mutation.

**Limitations:** Nested-pump guard: calling this tool from within an active pump on the same
thread is rejected with `DISPATCHER_BUSY` immediately. Does not guarantee that async
`Task`-based operations (not marshalled back to the Dispatcher) have completed.

**Applies to:** WPF Dispatcher and composition rendering pipeline.

---

## wpf_resolve_binding

Resolve the full data-binding chain for a dependency property on a WPF element.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element. |
| `propertyName` | string | *(required)* | Dependency property name to resolve the binding for (e.g. `"Text"`, `"IsEnabled"`). |

**Returns:**

```json
{
  "path": "SelectedSession.User.Name",
  "sourceTypeName": "MyApp.ViewModel.SessionViewModel",
  "sourceValue": "Alice",
  "pathSteps": [
    { "segment": "SelectedSession", "value": "SessionDto{...}" },
    { "segment": "User", "value": "UserDto{...}" },
    { "segment": "Name", "value": "Alice" }
  ],
  "converterTypeName": null,
  "converterParameter": null,
  "mode": "TwoWay",
  "validationErrors": [],
  "status": "OK"
}
```

`status` values: `"OK"`, `"PathError"`, `"ValidationError"`, `"MissingDataContext"`,
`"ConverterError"`, `"NoBinding"`.

**Guidelines:** This is the single binding-inspection tool. It returns full chain diagnostics
including per-step values and validation errors.

**Limitations:** Resolution is read-only and point-in-time. Converter implementations are
not invoked; only the converter type name is reported. Multi-bindings report child binding
chains individually.

**Applies to:** Any DependencyProperty with a data binding expression on any FrameworkElement.

---

## wpf_wait_for_property

Poll a WPF element property until its value equals `expectedValue` (presence check), or until
the element disappears (absence check).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `locator` | string | *(required)* | WpfLocator string identifying the element (e.g. `"$name:myButton"` or `"$type:Button"`). |
| `propertyName` | string | *(required)* | Property name to observe (e.g. `"IsEnabled"`, `"Text"`, `"Visibility"`). |
| `expectedValue` | string | *(none)* | Expected property value as a string. Required when `presenceExpected=present`; ignored when `presenceExpected=absent`. |
| `timeoutMs` | integer | `5000` | Timeout in milliseconds before the call fails with `DISPATCHER_BUSY`. |
| `presenceExpected` | string | `"present"` | `"present"`: wait until `propertyName` equals `expectedValue`. `"absent"`: wait until the element disappears. |

**Returns:**

```json
{
  "conditionMet": true,
  "actualValue": "True",
  "elapsedMs": 340,
  "pollCount": 4
}
```

**Guidelines:** Use `presenceExpected=absent` to detect modal dismissal or element removal.
Call `wpf_pump_until_idle` first when the property change is triggered by a UI mutation, to
avoid polling before the change propagates.

**Limitations:** On timeout, throws `DISPATCHER_BUSY` with a suggestion to call
`wpf_pump_until_idle` first. Polls on a background timer; the poll interval is approximately
80 ms.

**Applies to:** Any DependencyProperty on any element locatable by a WpfLocator expression.

---

## wpf_fetch_blob

Retrieve a large binary payload (e.g. screenshot PNG) from the in-process blob store by reference key.

Some tools store large payloads out-of-band and return a `blobRef` key instead of
inlining the bytes. Use this tool to retrieve the actual content.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `key` | string | *(required)* | The `blobRef` key returned by a previous tool call. |

**Returns:** A multi-content MCP response:

- Content block 0: JSON text with metadata:

  ```json
  { "key": "blob-a1b2c3", "mimeType": "image/png", "sizeBytes": 45678 }
  ```

- Content block 1: The raw payload — PNG `ImageContent` for images, UTF-8 `TextContent`
  for everything else.

**Blob lifetime:** Configured per session via `SnoopAgentOptions.BlobTtl`; default is 60 seconds.
After expiry the `key` is invalid and `BLOB_NOT_FOUND` is returned.
Re-run the originating tool to get a fresh ref.

---

## wpf_double_click

Fire a WPF routed double-click on an element (L1).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the element to double-click. |

**Returns:** `StateDeltaDto` with `success`, `stateChanged`, `chosenTier`, and `warnings[]` when the
fallback path was taken.

**Behavior:** The primary path raises `MouseLeftButtonDown` + `MouseLeftButtonUp` twice with
`ClickCount=2` on the second pair, then raises `Control.MouseDoubleClickEvent`. When the primary
path does not set `Handled=true` and the control type is not known to respond to routed
double-click, a Win32 `SendInput` mouse sequence is used instead and a `DOUBLE_CLICK_FALLBACK`
warning is emitted.

**Guidelines:** Use for controls that open detail views, start edits, or navigate on double-click
(e.g. ListBoxItem, TreeViewItem, DataGrid row). For single-click controls prefer `wpf_click` (L1) or
`wpf_execute_command` (L0). Automation must be enabled (`EnableAutomation=true`).

**Limitations:** Controls relying on mouse-capture state, preview event sequencing, or hit-testing
may not respond to the routed-event primary path (the fallback handles this). Does not check
`IsEnabled` or `IsVisible` before invoking.

---

## wpf_get_list_items

Enumerate the realized item containers of an `ItemsControl`.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node ID of the ItemsControl whose realized items should be enumerated. |

**Returns:** an array of `{ index, nodeId, displayName, isSelected }`.

**Guidelines:** Use to inspect list contents, determine which item is selected, or obtain nodeIds
for individual item containers. For virtualized lists, call `wpf_select_item` with an `index` and
`scrollToRealize=true` first to force realization of specific items before calling this tool.

**Limitations:** Only realized containers are returned; virtualized items not yet scrolled into view
are omitted and appear as gaps in the index sequence. Non-`ItemsControl` elements fail with
`INVALID_ARGUMENT`.

**Applies to:** ListBox, ListView, ComboBox, TreeView, DataGrid, and any `ItemsControl` subclass.

---

## wpf_get_actionables

Compact list of currently-interactable controls in the visible visual tree.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `rootNodeId` | string | *(whole tree)* | Subtree root to scan within. Omit to scan from `Application.Current`. |
| `maxResults` | int | `100` | Maximum results. Max 200. |

**Returns:** each item carries `nodeId`, `kind`, `label`, `x:Name`, `AutomationId`, `type`, enabled
state, and an L0 hint (`hasCommandBinding=true` → prefer `wpf_execute_command` over `wpf_click`).
`truncated=true` when results were cut at `maxResults`.

**Guidelines:** Intended for LLM-driven navigation: call once per screen to see the action menu, then
call `wpf_click` / `wpf_set_text_value` / `wpf_execute_command` on the chosen nodeId. Cheaper and
lower-token than `wpf_get_visual_tree` when you only need to decide what to act on.

**Limitations:** Skips invisible controls (`Visibility != Visible`, `IsVisible=false`,
`ActualWidth`/`ActualHeight = 0`) and non-actionable controls (TextBlock, Image, Border, Grid, etc.).

---

## wpf_act_sequence

Execute an ordered list of action primitives in a single round-trip.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `steps` | ActionStepDto[] | *(required)* | Ordered steps. Each step is `{ type, nodeId, value? }` where `type ∈ { click, double_click, execute_command, set_text }`; `value` is required only for `set_text`. |
| `stopOnError` | bool | `true` | If true, abort at the first step returning `Success=false`. If false, run every step and report per-step outcome. |

**Returns:** `ActionSequenceResultDto` with `allSucceeded`, `stoppedAtIndex` (-1 on full success),
and a `Steps` list — each entry carries the step's type/nodeId, success flag, full `StateDeltaDto`,
and (on failure) `errorCode` + `errorMessage`.

**Guidelines:** Collapse multi-step navigation (click → set_text → click → …) into one call to
eliminate LLM round-trip overhead. Pair with `wpf_get_actionables` to plan the sequence from a single
tree snapshot. Mutation must be enabled (`EnableMutation=true`); each primitive enforces its own
MaxTier gate, and tier failures are reported in the per-step delta.

---

## wpf_act_until

Fire one action, then poll a property predicate server-side until it matches or the timeout elapses.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `action` | ActionStepDto | *(required)* | `{ type, nodeId, value? }` (same shape as a `wpf_act_sequence` step). |
| `predicate` | ActUntilPredicateDto | *(required)* | `{ targetNodeId, propertyName, expectedValue?, presenceExpected? }`. `presenceExpected ∈ { present (default), absent }`. |
| `timeoutMs` | int | `5000` | Maximum milliseconds to poll after the action fires. |

**Returns:** `ActUntilResultDto` with `actionResult` (the action's delta), `success` (action OK and
predicate met), `predicateMet`, `timedOut`, `actualValue` (last observed), `elapsedMs`, `pollCount`.

**Behavior:** `present` is satisfied when the target node resolves AND its property equals
`expectedValue`; `absent` is satisfied when the node fails to resolve (e.g. a dialog closed). Poll
interval is 50 ms. If the action fails, polling is skipped and `success=false`.

**Guidelines:** Collapse "click → loop `wpf_wait_for_property` until X" into a single call, keeping the
wait inside the agent process.

---

## Error Codes

All tool errors return a structured object:

```json
{
  "code": "NODE_NOT_FOUND",
  "message": "Element with ID 0:42 no longer exists",
  "suggestion": "Re-navigate from wpf_get_windows — element was likely garbage collected"
}
```

| Code | Description | Suggested Recovery |
|------|-------------|-------------------|
| `NODE_NOT_FOUND` | The node ID does not exist or was garbage-collected. | Call `wpf_get_windows` and re-navigate the tree. |
| `DISPATCHER_BUSY` | The WPF Dispatcher did not accept work within the timeout. | Retry; the app may be running a long UI operation. |
| `OPERATION_TIMED_OUT` | The Dispatcher accepted the work but it did not complete in time. | Reduce scope (smaller subtree, fewer properties) or retry when idle. |
| `PROPERTY_READ_ONLY` | The property cannot be set. | Use `wpf_get_properties` to find writable properties. |
| `TYPE_CONVERSION_FAILED` | The value string could not be converted. | Check value format; see the `wpf_set_property` format table. |
| `UNSUPPORTED_PROPERTY_TYPE` | The property type is not in the safe-settable list. | Only primitive and common WPF value types are settable. |
| `MutationDisabled` | `wpf_set_property` called but mutations are disabled. | Set `EnableMutation = true` in `SnoopAgentOptions`. |
| `PROPERTY_REDACTED` | The property is sensitive and redacted. | Its value cannot be read or set. |
| `SESSION_NOT_FOUND` | No active session (target process likely exited). | Re-attach with a new `snoop-mcp` invocation. |
| `PROTOCOL_MISMATCH` | Agent and host protocol versions differ. | Update to matching versions. |
| `ELEMENT_NOT_RENDERABLE` | Element has zero size or is not visible. | Try `wpf_get_windows` for a full window screenshot. |
| `BLOB_NOT_FOUND` | The blob ref key has expired (default 60 s TTL) or was never issued. | Re-run the originating tool to get a fresh ref. |
| `LOCATOR_AMBIGUOUS` | The WpfLocator or item identifier matched more than one element. | Use a more specific locator (e.g. `$name:` or an index). |
| `LOCATOR_INVALID` | The WpfLocator string could not be parsed. | Check locator syntax; valid prefixes are `$name:`, `$type:`, `$id:`. |

### failureReason values (action tools)

Action tools (`wpf_click`, `wpf_execute_command`, `wpf_expand_collapse`, `wpf_toggle`,
`wpf_set_check_state`, `wpf_select_item`, `wpf_set_text_value`, `wpf_set_slider_value`)
return a `failureReason` string in their response object rather than a top-level error when
the call is structurally valid but cannot proceed. The `success` field is `false` and
`suggestion` contains recovery guidance.

| failureReason | Meaning | Typical cause | Suggestion |
|---------------|---------|---------------|-----------|
| `CannotExecuteCommand` | `CanExecute` returned `false` for the bound `ICommand`. | The command is not executable in the current application state (e.g. nothing is selected, form is invalid). | Check application state first; use `wpf_get_properties` to inspect relevant state properties before retrying. |
| `PatternNotSupported` | The element does not expose the required UI Automation pattern. | Invoking `wpf_click` on a non-invokable element (e.g. plain `TextBlock`, `Image`), or `wpf_expand_collapse` / `wpf_toggle` on an element whose peer does not implement the pattern. | Use `wpf_inspect_element` to confirm the element type; choose the appropriate L0 tool (`wpf_execute_command`, `wpf_set_check_state`) instead. |
| `ElementDisabled` | The UI Automation pattern is supported but the element is disabled (`IsEnabled=false`). | Attempting to invoke, toggle, or expand/collapse a disabled control. Distinct from `PatternNotSupported` (pattern is present but element is not actionable). | Verify `IsEnabled` with `wpf_inspect_element` before acting; the application may need to be in a different state. |
