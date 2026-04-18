# Consuming InitialForce.SnoopAgent (GitHub Packages)

> **Feed location note.** 1.0.0-rc.x is published to the **GitHub Packages**
> feed for the `InitialForce` org, *not* nuget.org. Publishing to nuget.org is
> currently blocked by a pending prefix-reservation issue on the
> `InitialForce.*` namespace. See the consumer setup below for how to wire up
> the GitHub feed.

## Quick start

```bash
# 1) Drop the feed entry into a nuget.config beside your .sln (see below).
# 2) Add the package:
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.2 --prerelease
```

`InitialForce.SnoopAgent` embeds an MCP server into your WPF application so any
MCP-compatible AI agent (Claude Desktop, Claude Code, Cursor, etc.) can inspect
the running UI without a debugger. See [`samples/minimal/`](../samples/minimal/)
for a complete working example.

## Feed + auth setup (GitHub Packages)

GitHub Packages requires an authenticated feed even when the packages are
public. Create a `nuget.config` next to your solution with the snippet below.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="initialforce" value="https://nuget.pkg.github.com/InitialForce/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="initialforce">
      <package pattern="InitialForce.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <initialforce>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </initialforce>
  </packageSourceCredentials>
</configuration>
```

Then set two environment variables in your shell (or your CI secrets):

```bash
export GITHUB_USER=<your-github-login>
export GITHUB_TOKEN=<classic-PAT-with-read:packages-scope>
```

Create the PAT at <https://github.com/settings/tokens/new> with the
`read:packages` scope. That's all it needs — no `repo` or `write:packages`.
Tokens can be scoped to a single org via fine-grained PATs if you prefer.

In GitHub Actions inside the `InitialForce` org, the built-in `GITHUB_TOKEN`
already carries `read:packages`; just reference it instead of a PAT.

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

### Installing from GitHub Packages

Configure the feed as shown at the top of this doc, then:

```bash
dotnet add package InitialForce.SnoopAgent --version 1.0.0-rc.2 --prerelease
```

To update to a later prerelease, bump the `--version` value.

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
