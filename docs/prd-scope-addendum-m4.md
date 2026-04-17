# PRD Scope Addendum — M4 (27-tool surface)

> **Relates to**: `PRD-v5-MVP.md` §5 (tool surface, scope-frozen at 22).
> **Reason**: The implementation ships 27 tools. Five tools present in the
> v3 PRD and `docs/mcp-tools-reference.md` were not carried forward into the
> PRD-v5-MVP scope table. They were implemented as part of the v3 foundation
> work and are retained in the shipped surface. This addendum records their
> Guidelines / Limitations / Applies-to per the W3-E1 format used in §5.2.

---

## The 5 additional tools (v3 carry-forward)

These tools appear in `docs/mcp-tools-reference.md` but are not listed in
PRD-v5-MVP.md §5.1–5.3. They are not new scope — they are shipped as part of
the v3 inspect toolset that v5-MVP inherited.

| # | Tool | Category | Purpose |
|---|------|----------|---------|
| 23 | `wpf_get_binding_info` | Observe | Lightweight binding summary for a single property. |
| 24 | `wpf_run_diagnostics` | Observe | Run all diagnostic providers on the visual tree. |
| 25 | `wpf_get_resources` | Observe | Walk the resource-dictionary hierarchy with precedence ordering. |
| 26 | `wpf_get_triggers` | Observe | All triggers on an element (Style, ControlTemplate, DataTemplate, direct). |
| 27 | `wpf_get_behaviors` | Observe | Attached Blend behaviors and actions (both Interactivity and Microsoft.Xaml.Behaviors). |

---

### wpf_get_binding_info

- **Guidelines**: Use for a quick binding summary (path, mode, status, error)
  without the full chain evaluation cost of `wpf_resolve_binding`. Prefer
  `wpf_resolve_binding` when per-step intermediate values are needed.
- **Limitations**: Does not walk through intermediate converters or
  multi-binding children at per-step resolution. Read-only; point-in-time.
- **Applies to**: Any `DependencyProperty` on any `FrameworkElement` that
  has a data-binding expression.

---

### wpf_run_diagnostics

- **Guidelines**: Run after tree inspection to surface binding errors,
  non-virtualized lists, and layout issues automatically. Use `minLevel`
  to suppress informational noise during debugging sessions.
- **Limitations**: Cursor pagination is supported; results sorted by
  severity (Critical first). Providers cannot be extended by the agent
  at runtime — the set is fixed at build time.
- **Applies to**: The full visual tree, or any subtree scoped via a
  `nodeId` parameter. Runs all built-in Snoop diagnostic providers.

---

### wpf_get_resources

- **Guidelines**: Use to understand resource override chains (e.g. a
  local style shadowing an application-level style). Closest-scope entry
  appears first; shadowed entries include `origin: "Shadowed"`.
- **Limitations**: Only reads from `FrameworkElement.Resources` and the
  application resource dictionary. Dynamic resources are resolved at
  call time; stale values are possible if the resource changes between
  calls. Cursor pagination applies; `take` max 200.
- **Applies to**: Any `FrameworkElement` node, or the application root
  when `nodeId` is omitted.

---

### wpf_get_triggers

- **Guidelines**: Use to understand why an element's visual appearance or
  behaviour changes under certain conditions. Inspect `isActive` to see
  which triggers are currently firing.
- **Limitations**: Returns the static trigger list only; the `isActive`
  field reflects the current state at call time. DataTrigger bindings
  are not resolved to their source — use `wpf_get_binding_info` for that.
- **Applies to**: Any `FrameworkElement` that has triggers in its Style,
  ControlTemplate, DataTemplate, or directly on the element.

---

### wpf_get_behaviors

- **Guidelines**: Use to discover runtime behaviors attached via the Blend
  SDK or `Microsoft.Xaml.Behaviors.Wpf`. Properties of each behavior are
  enumerated via reflection.
- **Limitations**: Works only with `System.Windows.Interactivity` (legacy
  Blend SDK) and `Microsoft.Xaml.Behaviors.Wpf` (modern package). Custom
  behavior host patterns that do not use either SDK are not detected.
  Behavior properties are read-only through this tool.
- **Applies to**: Any `UIElement` that has behaviors attached via either
  supported SDK.

---

## Revised total: 27 tools

| Group | Count | Tools |
|-------|-------|-------|
| Observe (v3 core) | 9 | `wpf_get_session_info`, `wpf_get_windows`, `wpf_get_visual_tree`, `wpf_get_children`, `wpf_get_ancestors`, `wpf_find_elements`, `wpf_inspect_element`, `wpf_get_properties`, `wpf_capture_screenshot` |
| Observe (v3 carry-forward) | 5 | `wpf_get_binding_info`, `wpf_run_diagnostics`, `wpf_get_resources`, `wpf_get_triggers`, `wpf_get_behaviors` |
| Act L0 | 5 | `wpf_execute_command`, `wpf_set_text_value`, `wpf_set_check_state`, `wpf_select_item`, `wpf_set_property` |
| Act L1 | 3 | `wpf_click`, `wpf_toggle`, `wpf_expand_collapse` |
| Extract | 1 | `wpf_resolve_binding` |
| Utility | 4 | `wpf_pump_until_idle`, `wpf_wait_for_property`, `wpf_poll_changes`, `wpf_fetch_blob` |
| **Total** | **27** | |

The 5 v3 carry-forward tools are agent-visible (they appear in `tools/list`)
and count toward the surface, bringing the total from the PRD-v5-MVP baseline
of 22 to 27.
