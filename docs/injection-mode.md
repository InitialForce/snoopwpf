# Injection Mode — Inspecting Any Running WPF App

Injection mode allows you to attach the SnoopWPF MCP agent to any running WPF process
without modifying it. Two executables are provided:

- **`snoop-mcp.exe`** — injects the agent and exposes an MCP server on stdio.
  This is what AI clients (Claude Code, Claude Desktop) use.
- **`snoop-cli.exe`** — command-line interface for human-driven inspection and scripting.

---

## Prerequisites

The target process must be running one of:

- .NET Framework 4.6.2 or later
- .NET 6.0 or later (6, 7, 8, 9, 10+)

Self-contained single-file applications are not supported (no injectable .NET runtime
handle). The agent itself requires .NET 8.0 to run on the host machine.

---

## snoop-mcp.exe — MCP Server

`snoop-mcp` injects the agent, waits for it to connect over a named pipe, performs a
cryptographic handshake, and then runs an MCP server on stdio. The AI client sees a
normal MCP server.

### Usage

```
snoop-mcp [--pid <pid>] [--window-title <pattern>] [--timeout <seconds>] [--verbose]
```

| Flag | Description |
|------|-------------|
| `--pid <pid>` | Target process ID. Mutually exclusive with `--window-title`. |
| `--window-title <pattern>` | Find target by window title (case-insensitive substring). |
| `--timeout <seconds>` | Handshake timeout. Default: 30. |
| `--verbose` | Log diagnostic messages to stderr. |

Exactly one of `--pid` or `--window-title` is required.

### Examples

Inject by PID:

```
snoop-mcp --pid 12345
```

Inject by window title:

```
snoop-mcp --window-title "My Application"
```

Verbose output (useful for diagnosing connection issues):

```
snoop-mcp --pid 12345 --verbose
```

---

## Integrating with MCP Clients

### Claude Code — project-level config

Create `.mcp.json` in your project root:

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

Replace `12345` with the actual PID each time you start your app. To avoid updating
this manually, use `--window-title`:

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

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "C:/tools/snoop-mcp.exe",
      "args": ["--window-title", "My Application"]
    }
  }
}
```

Windows path: `%APPDATA%\Claude\claude_desktop_config.json`

---

## snoop-cli.exe — Command-Line Interface

`snoop-cli` performs the same injection as `snoop-mcp` but exposes a human-readable
command-line interface instead of an MCP server. Use it for scripting, CI diagnostics,
or quick manual inspection.

### Verbs

```
snoop-cli <verb> --pid <pid> [--window-title <pattern>] [--json] [options]
```

| Verb | Description |
|------|-------------|
| `list` | List top-level WPF windows |
| `tree` | Show visual tree |
| `props <node-id>` | Show properties of a node |
| `inspect <node-id>` | Show rich element summary |
| `find` | Find elements by type or name |
| `diag` | Run diagnostics |
| `screenshot [node-id]` | Capture screenshot to file |

### Examples

List windows of a process:

```
snoop-cli list --pid 12345
```

Show visual tree (depth 4):

```
snoop-cli tree --pid 12345 --depth 4
```

Show properties of a node:

```
snoop-cli props 0:42 --pid 12345
snoop-cli props 0:42 --pid 12345 --filter Text
```

Inspect a single element:

```
snoop-cli inspect 0:42 --pid 12345
```

Find all TextBox elements:

```
snoop-cli find --pid 12345 --type TextBox
```

Run diagnostics:

```
snoop-cli diag --pid 12345
```

Capture a screenshot:

```
snoop-cli screenshot --pid 12345 --output window.png
snoop-cli screenshot 0:42 --pid 12345 --output element.png
```

All verbs support `--json` for machine-readable output:

```
snoop-cli list --pid 12345 --json
```

---

## How Injection Works

1. `snoop-mcp` generates a 256-bit random session token and a random GUID pipe name.
2. It writes both to a temporary settings file with owner-only file ACLs.
3. It creates a named pipe server (current-user-only ACL) and waits for connection.
4. The InjectorLauncher loads the agent DLL into the target process.
5. The injected agent reads the settings file, deletes it immediately, and zeroes
   the token from memory after reading.
6. The agent connects to the pipe and the host sends a handshake challenge containing
   the session token.
7. The agent responds with its capabilities and echoes the session token.
8. The host verifies the token and the client PID via `GetNamedPipeClientProcessId()`.
9. All subsequent MCP tool calls are proxied through the pipe.

---

## Security Considerations

**Process ownership check.** Before injection, `snoop-mcp` verifies that the target
process is owned by the same Windows user. If ownership mismatches, injection is
refused. This prevents one user from inspecting another user's processes.

**Pipe security.** The pipe name is an unpredictable GUID. On .NET 6+,
`PipeOptions.CurrentUserOnly` restricts connections to the current user. On .NET
Framework, `PipeSecurity`/`PipeAccessRule` provides the same guarantee.

**Session token.** A 256-bit token generated by `RandomNumberGenerator` is used to
authenticate the injected agent. The token is never logged, never appears in URLs or
query strings, and is zeroed from memory after the handshake.

**Settings file.** The temporary settings file containing the pipe name and token is
written with an owner-only DACL and is deleted by the injected agent immediately after
reading. If the agent never runs, `snoop-mcp` cleans it up on exit.

See [Security](security.md) for the complete threat model.

---

## Troubleshooting

### "Could not locate target process"

- Verify the PID is correct and the process is still running.
- If using `--window-title`, check spelling — it is a substring match.

### "Target process is owned by a different user"

Injection is blocked when the target process runs under a different account. Either
run `snoop-mcp` as the same user, or — if you understand the risk — use `--force`
(not yet implemented in v1).

### "Timed out waiting for agent to connect"

Increase `--timeout` (default 30 seconds). For very slow machines or heavily loaded
apps, the injected DLL may take longer to start. Check `--verbose` for details.

### Agent connects but tools fail

Check `--verbose` output. Common causes:
- The target process crashed after injection.
- The target's .NET runtime is trimmed or single-file (not supported).
- Dispatcher deadlock — the target app's UI thread is blocked.
