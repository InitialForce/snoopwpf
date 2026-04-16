# MCP Tools Reference

All 15 tools exposed by SnoopWPF.Agent. Tool names are prefixed with `wpf_`.

Error responses follow a common schema — see [Error Codes](#error-codes) at the bottom.

---

## wpf_get_session_info

Get information about the inspected process.

**Parameters:** none

**Returns:**

```json
{
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
  "capabilities": ["tree", "properties", "diagnostics", "resources", "screenshots"]
}
```

**Usage:** Call this first to verify connection and to get window node IDs for
subsequent calls.

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
  "error": "MUTATION_DISABLED"
}
```

---

## wpf_get_binding_info

Get detailed data binding information for a specific property.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nodeId` | string | *(required)* | Node to inspect. |
| `propertyName` | string | *(required)* | Property name (e.g. `"Text"`, `"IsEnabled"`). |

**Returns:**

```json
{
  "hasBinding": true,
  "bindingType": "Binding",
  "path": "UserName",
  "elementName": null,
  "relativeSource": null,
  "mode": "TwoWay",
  "updateSourceTrigger": "PropertyChanged",
  "converterTypeName": null,
  "sourceType": "MyApp.ViewModel.MainViewModel",
  "status": "Active",
  "error": null,
  "dataContextIsNull": false,
  "dataContextType": "MyApp.ViewModel.MainViewModel",
  "resolvedValue": "Alice",
  "childBindings": null
}
```

`status` values: `"Active"`, `"PathError"`, `"UpdateTargetError"`, `"UpdateSourceError"`,
`"Detached"`, `"Unattached"`.

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

**Returns:** A multi-content MCP response:

- Content block 0: JSON text with metadata:

  ```json
  { "width": 800, "height": 600, "nodeId": "0:1" }
  ```

- Content block 1: PNG image as MCP `ImageContent`.

No temp files are written. The PNG bytes are embedded directly in the MCP response.

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
| `MUTATION_DISABLED` | `wpf_set_property` called but mutations are disabled. | Set `EnableMutation = true` in `SnoopAgentOptions`. |
| `PROPERTY_REDACTED` | The property is sensitive and redacted. | Its value cannot be read or set. |
| `SESSION_NOT_FOUND` | No active session (target process likely exited). | Re-attach with a new `snoop-mcp` invocation. |
| `PROTOCOL_MISMATCH` | Agent and host protocol versions differ. | Update to matching versions. |
| `ELEMENT_NOT_RENDERABLE` | Element has zero size or is not visible. | Try `wpf_get_windows` for a full window screenshot. |
