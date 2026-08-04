# SnoopWPF.Agent

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server for WPF
applications. Enables AI agents (Claude Code, Claude Desktop) to inspect the visual
tree, read properties, diagnose binding errors, and capture screenshots of your
running WPF app.

**Requires .NET 8.0+.** For older targets, use the injection-mode `snoop-mcp.exe`.

## Quick Start

### 1. Add the package

```xml
<PackageReference Include="SnoopWPF.Agent" />
```

### 2. Start the agent

```csharp
// App.xaml.cs
using SnoopWPF.Agent.Server;

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    SnoopAgent.StartCoLocated(); // stdio transport by default
}
```

### 3. Configure your MCP client

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

## Options

```csharp
SnoopAgent.StartCoLocated(new SnoopAgentOptions
{
    Transport      = TransportMode.Stdio,  // or TransportMode.Pipe
    EnableMutation = false,                // set true to allow wpf_set_property
    EnableRedaction = true,                // redact sensitive properties
    TimeoutMs      = 5000,                // per-operation Dispatcher timeout
});
```

## Available Tools

`wpf_diagnostics`, `wpf_get_windows`, `wpf_get_visual_tree`, `wpf_get_children`,
`wpf_get_ancestors`, `wpf_find_elements`, `wpf_inspect_element`, `wpf_get_properties`,
`wpf_set_property`, `wpf_run_diagnostics`, `wpf_get_resources`,
`wpf_capture_screenshot`, `wpf_get_triggers`, `wpf_get_behaviors`, `wpf_fetch_blob`,
`wpf_poll_changes`, `wpf_pump_until_idle`, `wpf_resolve_binding`, `wpf_click`,
`wpf_execute_command`, `wpf_expand_collapse`, `wpf_select_item`, `wpf_set_check_state`,
`wpf_set_slider_value`, `wpf_set_text_value`, `wpf_toggle`, `wpf_wait_for_property`.

## Documentation

Full documentation: [docs/mcp-agent.md](../docs/mcp-agent.md)

- [NuGet Mode deep dive](../docs/nuget-mode.md)
- [MCP Tools Reference](../docs/mcp-tools-reference.md)
- [Security Model](../docs/security.md)
