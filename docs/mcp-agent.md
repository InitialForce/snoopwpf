# SnoopWPF.Agent — MCP Agent for WPF

SnoopWPF.Agent is maintained in [InitialForce's fork](https://github.com/InitialForce/snoopwpf)
of [SnoopWPF](https://github.com/snoopwpf/snoopwpf). It extends the canonical Snoop
WPF spying utility with an [MCP (Model Context Protocol)](https://modelcontextprotocol.io/)
server, enabling AI agents — primarily Claude Code and Claude Desktop — to inspect,
debug, and interact with running WPF applications programmatically.

## Overview

The agent exposes Snoop's inspection engine as 27 MCP tools. An AI agent can navigate
the visual tree, read property values, diagnose binding errors, capture screenshots,
interact with UI elements, and (optionally) mutate property values — all without a
human operating Snoop's GUI.

Both modes can run simultaneously with the full Snoop UI window.

## Two Integration Modes

### NuGet / Compile-in Mode

Your WPF app references the `SnoopWPF.Agent` NuGet package and calls `SnoopAgent.StartCoLocated()`
at startup. The MCP server runs in-process alongside your application.

- Requires .NET 8+
- Transport: stdio (subprocess) or named pipe
- No injection, no cross-process IPC complexity
- Token-free when using stdio transport (process boundary is the security boundary)

### Injection Mode

An external process (`snoop-mcp.exe`) injects the agent into any running WPF process.
The injected DLL communicates back over a named pipe; `snoop-mcp.exe` then exposes MCP
on stdio to the AI client.

- Supports .NET Framework 4.6.2 and .NET 6/7/8/9+
- No changes to the target application required
- Injection restricted to processes owned by the current user

---

## Quick Start: NuGet Mode

### 1. Add the package

```xml
<PackageReference Include="SnoopWPF.Agent" Version="*" />
```

### 2. Call StartCoLocated() in your App

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    SnoopAgent.StartCoLocated(); // default: stdio transport
}
```

The server is ready as soon as `StartCoLocated()` returns. Dispose the handle to stop it early:

```csharp
var handle = SnoopAgent.StartCoLocated();
// ... later:
handle.Dispose();
```

### 3. Configure your MCP client

**Claude Code (`~/.claude/mcp_servers.json` or `.mcp.json` in project root):**

```json
{
  "mcpServers": {
    "snoop": {
      "command": "dotnet",
      "args": ["run", "--project", "MyApp/MyApp.csproj"],
      "env": {}
    }
  }
}
```

When using the Pipe transport, first start your app and then point the client at the
pipe name (see [NuGet Mode deep dive](nuget-mode.md)).

---

## Quick Start: Injection Mode

### 1. Download snoop-mcp.exe

Get it from the [GitHub releases page](https://github.com/snoopwpf/snoopwpf/releases).

### 2. Find the target PID

```powershell
Get-Process MyWpfApp
```

### 3. Configure Claude Code

```json
{
  "mcpServers": {
    "snoop": {
      "command": "snoop-mcp",
      "args": ["--pid", "12345"]
    }
  }
}
```

Or by window title:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "snoop-mcp",
      "args": ["--window-title", "My Application"]
    }
  }
}
```

`snoop-mcp` injects itself into the target, handshakes over a named pipe, and then
runs an MCP server on stdio. The AI client sees a normal MCP server.

---

## MCP Client Configuration Examples

### Claude Desktop

Add to `claude_desktop_config.json` (location varies by OS):

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "snoop-mcp",
      "args": ["--pid", "12345"]
    }
  }
}
```

### Claude Code (project-level)

Create `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "snoop-mcp",
      "args": ["--window-title", "MyApp"]
    }
  }
}
```

---

## Available Tools (27)

All tools return structured JSON. Errors include an error code, a human-readable
message, and a `suggestion` field to help the agent recover.

| Tool | Description |
|------|-------------|
| `wpf_get_session_info` | Process name, PID, .NET version, dispatchers, capabilities |
| `wpf_get_windows` | List top-level windows with node IDs and dimensions |
| `wpf_get_visual_tree` | Depth-limited tree dump (max 5000 nodes) |
| `wpf_get_children` | Cursor-paginated direct children of a node |
| `wpf_get_ancestors` | Ancestor chain from node to root |
| `wpf_find_elements` | Search by type, name, or property value |
| `wpf_inspect_element` | Rich summary of a single element |
| `wpf_get_properties` | Cursor-paginated property list with values and binding status |
| `wpf_set_property` | Set a property value (opt-in, disabled by default) |
| `wpf_get_binding_info` | Detailed binding debug info for one property |
| `wpf_run_diagnostics` | Binding errors, non-virtualized lists, and more |
| `wpf_get_resources` | Resource dictionary with precedence ordering |
| `wpf_capture_screenshot` | PNG screenshot stored as a blob; retrieve via `wpf_fetch_blob` |
| `wpf_get_triggers` | Style/Template/Element triggers on an element |
| `wpf_get_behaviors` | Attached behaviors (Interactivity + Microsoft.Xaml.Behaviors) |
| `wpf_click` | Invoke the primary click action via UI Automation InvokePattern (L1) |
| `wpf_execute_command` | Execute the `ICommand` bound to a WPF element — L0, no Win32 input (preferred over `wpf_click`) |
| `wpf_expand_collapse` | Expand or collapse an element via UI Automation ExpandCollapsePattern (L1) |
| `wpf_select_item` | Select an item in a `ListBox`, `ComboBox`, or any `Selector` control (L0) |
| `wpf_set_text_value` | Set text content of a `TextBox`, `PasswordBox`, or `RichTextBox` via `SetCurrentValue` (L0) |
| `wpf_set_check_state` | Set checked/unchecked/indeterminate state of a `CheckBox` or `RadioButton` (L0) |
| `wpf_toggle` | Flip the toggle state via UI Automation TogglePattern (L1) |
| `wpf_poll_changes` | Non-blocking structural-change detection; returns added/removed node IDs and current tree version |
| `wpf_pump_until_idle` | Wait until the WPF Dispatcher and composition pipeline are simultaneously idle |
| `wpf_resolve_binding` | Resolve the full data-binding chain for a dependency property, including per-step values |
| `wpf_wait_for_property` | Poll an element property until it equals an expected value or the element disappears |
| `wpf_fetch_blob` | Retrieve a large binary payload (e.g. screenshot PNG) from the in-process blob store by ref |

See the [MCP Tools Reference](mcp-tools-reference.md) for full parameter lists and
example responses.

---

## Security Model

- **Localhost only.** The server binds to `127.0.0.1`. No configurable hostname.
- **Mutations disabled by default.** `wpf_set_property` returns `MUTATION_DISABLED`
  unless `EnableMutation = true` in `SnoopAgentOptions`.
- **Sensitive property redaction.** Properties containing keywords like `password`,
  `secret`, `apikey`, `connectionstring`, etc. (21 keywords total) are never read —
  the getter is not invoked and `[REDACTED]` is returned.
- **Pipe security.** In injection mode, the pipe has a random GUID name, a
  current-user-only ACL, and a 256-bit session token for handshake authentication.
- **Process ownership check.** Injection is refused if the target process is owned
  by a different user (requires `--force` to override).

See [Security](security.md) for the full security model.

---

## Limitations

- Windows only (WPF is Windows-only).
- NuGet mode requires .NET 8+. Older apps use injection mode.
- MCP server binds to `127.0.0.1` only — not accessible remotely by design.
- `wpf_capture_screenshot` may return an error for zero-size or hidden elements.
- Self-contained single-file applications are not injectable (no .NET runtime handle).
- Method invocation (`wpf_invoke_method`) is deferred to a future version.

---

## Further Reading

- [NuGet Mode](nuget-mode.md) — embedding the agent in your WPF app
- [Injection Mode](injection-mode.md) — external agent injection
- [MCP Tools Reference](mcp-tools-reference.md) — all 27 tools with examples
- [Security](security.md) — security model and threat mitigations
