# SnoopWPF.Agent.Tools

MCP tool implementations for SnoopWPF Agent. Contains the 28 `wpf_*` tool classes
registered via `[McpServerToolType]` that are exposed over the MCP server.

**Targets: .NET 8.0-windows**
**Not intended for direct consumption — bundled into `SnoopWPF.Agent`.**

## Available Tools

| Tool | Description |
|------|-------------|
| `wpf_get_session_info` | Process/session metadata, capabilities |
| `wpf_get_windows` | List all top-level WPF windows |
| `wpf_get_visual_tree` | Walk the visual tree |
| `wpf_get_children` | Get direct children of an element |
| `wpf_get_ancestors` | Get ancestor chain of an element |
| `wpf_find_elements` | Find elements by type/name/locator |
| `wpf_inspect_element` | Detailed element inspection |
| `wpf_get_properties` | Read element properties |
| `wpf_set_property` | Write element property (requires `EnableMutation=true`) |
| `wpf_get_binding_info` | Inspect data bindings |
| `wpf_resolve_binding` | Resolve binding source object |
| `wpf_run_diagnostics` | Run binding error diagnostics |
| `wpf_get_resources` | Inspect resource dictionaries |
| `wpf_capture_screenshot` | Capture element/window screenshot |
| `wpf_fetch_blob` | Retrieve screenshot blob data |
| `wpf_get_triggers` | Inspect element triggers |
| `wpf_get_behaviors` | Inspect attached behaviors |
| `wpf_poll_changes` | Subscribe to property change events |
| `wpf_pump_until_idle` | Wait for the dispatcher to settle |
| `wpf_wait_for_property` | Wait for a property to reach a target value |
| `wpf_click` | Click a UI element |
| `wpf_execute_command` | Execute an ICommand |
| `wpf_expand_collapse` | Expand or collapse a tree item |
| `wpf_select_item` | Select a list/combo item |
| `wpf_set_check_state` | Set checkbox/toggle state |
| `wpf_set_slider_value` | Set slider value |
| `wpf_set_text_value` | Set text in a TextBox |
| `wpf_toggle` | Toggle a toggle button |

## Notes

This package is an internal implementation detail bundled into `SnoopWPF.Agent`.
Consumers should reference `SnoopWPF.Agent` directly.

## Documentation

- [MCP Tools Reference](../docs/mcp-tools-reference.md)
- [SnoopWPF.Agent](../SnoopWPF.Agent.Server/README.md)
