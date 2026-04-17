# Getting Started with SnoopWPF.Agent

SnoopWPF.Agent exposes Snoop's WPF inspection engine as an [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server, so AI agents such as Claude Desktop and Claude Code can inspect live WPF applications.

## Connect Claude Desktop in 60 seconds (Injection mode)

No code changes required — inject into any running WPF process:

**Step 1 — Download `snoop-mcp.exe`** from the [GitHub releases page](https://github.com/InitialForce/snoopwpf/releases).

**Step 2 — Find your app's PID:**

```powershell
Get-Process MyWpfApp
```

**Step 3 — Add to `claude_desktop_config.json`:**

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "C:\\tools\\snoop-mcp.exe",
      "args": ["--pid", "12345"]
    }
  }
}
```

Config file location:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Restart Claude Desktop, then ask it: *"What WPF windows are open in my app?"*

---

## Alternative: target by window title

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "C:\\tools\\snoop-mcp.exe",
      "args": ["--window-title", "My Application"]
    }
  }
}
```

This re-injects on each session start — no need to update the PID.

---

## NuGet / Compile-in mode (apps you own)

For apps you can recompile, embed the agent directly. No injection required.

### 1. Add the package

```xml
<PackageReference Include="SnoopWPF.Agent" Version="*" />
```

Requires .NET 8+. For older runtimes use [Injection mode](injection-mode.md).

### 2. Start at app startup

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    SnoopAgent.StartCoLocated(); // stdio transport; process boundary = security boundary
}
```

### 3. Configure Claude Code

Create `.mcp.json` at your project root:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "dotnet",
      "args": ["run", "--project", "MyApp/MyApp.csproj"]
    }
  }
}
```

Or configure globally in `~/.claude/mcp_servers.json`.

### 4. Configure Claude Desktop (NuGet mode)

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\MyApp\\MyApp.csproj"]
    }
  }
}
```

---

## Brokered mode (advanced)

When your app targets .NET Framework or needs a separate broker process, see
[Brokered Mode Integration](brokered-mode-integration.md).

---

## What can the agent do?

Once connected, the agent has access to 28 `wpf_*` tools:

| Category | Tools |
|---|---|
| Session | `wpf_get_session_info` |
| Navigation | `wpf_get_windows`, `wpf_get_visual_tree`, `wpf_get_children`, `wpf_get_ancestors`, `wpf_find_elements` |
| Inspection | `wpf_inspect_element`, `wpf_get_properties`, `wpf_get_binding_info`, `wpf_get_resources`, `wpf_get_triggers`, `wpf_get_behaviors` |
| Actions | `wpf_click`, `wpf_execute_command`, `wpf_expand_collapse`, `wpf_select_item`, `wpf_set_text_value`, `wpf_set_check_state`, `wpf_toggle` |
| Mutation | `wpf_set_property` (requires `EnableMutation = true`) |
| Diagnostics | `wpf_run_diagnostics`, `wpf_capture_screenshot`, `wpf_fetch_blob` |
| Async/polling | `wpf_poll_changes`, `wpf_pump_until_idle`, `wpf_wait_for_property` |
| Advanced | `wpf_resolve_binding`, `wpf_get_slider_range`, `wpf_set_slider_value`, `wpf_get_suggestions` |

Full reference: [MCP Tools Reference](mcp-tools-reference.md).

---

## Security notes

- **Read-only by default.** `wpf_set_property` is disabled unless you pass `EnableMutation = true` to `StartCoLocated()`.
- **Sensitive properties are redacted.** Names matching a keyword list (password, token, apikey, connectionstring, etc.) return `[REDACTED]` on all read paths.
- **Named-pipe transport uses a 256-bit session token** with constant-time verification.
- **Injection is restricted** to processes owned by the current user.

See [Security Model](security.md) for full details.
