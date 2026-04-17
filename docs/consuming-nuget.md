# Consuming InitialForce.SnoopAgent from NuGet.org

## Quick start

```bash
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.1 --prerelease
```

`InitialForce.SnoopAgent` embeds an MCP server into your WPF application so any
MCP-compatible AI agent (Claude Desktop, Claude Code, Cursor, etc.) can inspect
the running UI without a debugger. See [`samples/minimal/`](../samples/minimal/)
for a complete working example.

## Available packages

| Package | When to use |
|---|---|
| `InitialForce.SnoopAgent` | Primary: in-process MCP server in a WPF app (CoLocated mode). References and bundles Engine, Tools, Contracts, and Injection. |
| `InitialForce.SnoopAgent.Contracts` | DTOs and interfaces only. Reference this if you are implementing a custom `ISnoopInspector` or `ISuggestionTranslator`. |
| `InitialForce.SnoopAgent.Analyzers` | Optional Roslyn analyzers for projects that consume the Agent. Source-only package, zero runtime overhead. |
| `InitialForce.SnoopAgent.Remote` | Wire-format contracts for brokered/injection mode. Reference this if you are building a custom transport. |
| `InitialForce.SnoopAgent.BrokerHost` | Out-of-process broker mode (external `snoop-mcp.exe` inspecting third-party WPF apps). Bundles Contracts, Remote, Tools, and Engine. |

## Pre-release rc.1 considerations

This is the first public release. Install it explicitly with the `--prerelease` flag:

```bash
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.1 --prerelease
```

The project is committed to SemVer from 1.0.0 onwards: major version bumps signal
breaking API changes, minor bumps are additive, patch bumps are bug fixes only.
The API surface may change slightly between rc.1 and the stable 1.0.0 release, but
breaking changes between rc.x and stable will be documented in the changelog.

Found a bug or unexpected behaviour? Please open an issue at
[github.com/InitialForce/snoopwpf/issues](https://github.com/InitialForce/snoopwpf/issues).

## Claude Desktop / Claude Code MCP config

### CoLocated mode (your app process serves MCP over stdio)

Your app calls `SnoopAgent.StartCoLocated()` at startup and the agent communicates
with Claude directly via stdio. Add this to your `.mcp.json` or
`%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "snoop-myapp": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/YourApp.csproj"]
    }
  }
}
```

For a pre-built executable replace `"dotnet"` / `"args"` with the path to your
compiled app and any required arguments.

### Brokered mode (standalone `snoop-mcp.exe` inspects a running process)

`snoop-mcp.exe` is included in the `InitialForce.SnoopAgent.BrokerHost` package.
Pass the target process ID at startup:

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

Replace `12345` with the actual PID of the WPF process you want to inspect.
The `snoop-mcp` executable must be on your `PATH` or you can use its full path.

## Local development workflow

### Installing from nuget.org

No custom `NuGet.config` is needed for 1.0.0-rc.1 and later — the packages are
published to the public nuget.org feed. Run:

```bash
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.1 --prerelease
```

To update to a later prerelease:

```bash
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.2 --prerelease
```

### Debugging with symbols (snupkg)

Symbol packages (`.snupkg`) are published to nuget.org alongside each release.
Visual Studio loads them automatically when you enable:

1. **Tools → Options → Debugging → General**
   - Check "Enable Source Server Support"
   - Check "Enable Source Link support"
2. **Tools → Options → Debugging → Symbols**
   - Add `https://symbols.nuget.org/download/symbols` to the symbol server list

After these settings are applied, stepping into `InitialForce.SnoopAgent` code
from the debugger will load the correct PDB and source automatically.

## Versioning and upgrade guide

### SemVer contract

| Version component | Meaning |
|---|---|
| Major | Breaking API change — update call sites |
| Minor | New functionality added in a backwards-compatible way |
| Patch | Bug fix — safe to update without code changes |

### Upgrading from `SnoopWPF.Agent.*` 6.x

The package IDs have changed from `SnoopWPF.Agent.*` to `InitialForce.SnoopAgent.*`,
but the C# namespaces are unchanged. To upgrade:

1. In each `.csproj` that references the old packages, rename the `PackageReference`:

   ```xml
   <!-- Before -->
   <PackageReference Include="SnoopWPF.Agent" Version="6.*" />

   <!-- After -->
   <PackageReference Include="InitialForce.SnoopAgent" Version="1.0.0-rc.1" />
   ```

2. No code changes are required — namespaces, type names, and method signatures are
   identical.

3. If you reference `SnoopWPF.Agent.Contracts`, `SnoopWPF.Agent.Remote`, or
   `SnoopWPF.Agent.BrokerHost`, rename those `PackageReference` entries the same way
   (`InitialForce.SnoopAgent.Contracts`, `InitialForce.SnoopAgent.Remote`,
   `InitialForce.SnoopAgent.BrokerHost`).
