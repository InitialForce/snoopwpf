# NuGet Packaging — Feed Decision

## Packages

| Package ID | Project | Purpose |
|---|---|---|
| `SnoopWPF.Agent` | `SnoopWPF.Agent.Server` | MCP server library (injected into WPF process) |
| `SnoopWPF.Agent.BrokerHost` | `SnoopWPF.Agent.BrokerHost` | Broker infrastructure for external host processes |
| `SnoopWPF.Agent.Remote` | `SnoopWPF.Agent.Remote` | Pipe client + framing for broker-side consumers |
| `SnoopWPF.Agent.Contracts` | `SnoopWPF.Agent.Contracts` | Shared DTOs, interfaces, WpfLocator |
| `SnoopWPF.Agent.Analyzers` | `SnoopWPF.Agent.Analyzers` | Roslyn analyzer SWPF0001 (development dependency) |

`SnoopWPF.Agent.Host` (`snoop-mcp.exe`, injection-mode executable) is intentionally **not packaged** as a NuGet library — it ships as a zip artifact instead.

## Feed Decision

**Initial (pre-stable):** GitHub Packages (`https://nuget.pkg.github.com/InitialForce/index.json`).

Rationale:
- Zero extra configuration — `GITHUB_TOKEN` covers auth in CI.
- Packages stay private to the org until the API surface stabilises.
- No nuget.org namespace squatting risk during development.

**After first stable release (v1.0.0):** Push to [nuget.org](https://www.nuget.org/) in addition to GitHub Packages.

Steps to flip:
1. Add `NUGET_ORG_API_KEY` as a repository secret.
2. In `.github/workflows/release.yml`, add a second `dotnet nuget push` step targeting `https://api.nuget.org/v3/index.json` with `--api-key ${{ secrets.NUGET_ORG_API_KEY }}`.
3. Reserve the package IDs on nuget.org before the first stable push.

## Signing

Signing is handled by [SignPath.io](https://signpath.io/) (free certificate already used by upstream snoopwpf). Signing is a post-pack step outside the `release.yml` workflow for now; add a `SignPath/github-action-submit-signing-request` step before the publish step once the signing policy is configured.

## Versioning

Packages are versioned from the git tag (strip leading `v`). The release workflow is triggered by tags matching `v*`. Pre-release versions use standard SemVer pre-release suffixes (e.g. `v1.0.0-beta.1`).
