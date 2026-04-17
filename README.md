# Snoop (MCP Agent Fork)

> This is [InitialForce](https://github.com/InitialForce)'s fork of
> [snoopwpf/snoopwpf](https://github.com/snoopwpf/snoopwpf), extending the canonical
> Snoop WPF spying utility with a Model Context Protocol (MCP) server so AI coding
> agents can inspect live WPF applications. The Snoop desktop UI is preserved
> unchanged. If you want the classic Snoop experience,
> use [upstream](https://github.com/snoopwpf/snoopwpf).

Snoop is an open source WPF spying utility originally created by [Pete Blois](https://github.com/peteblois) and is currently maintained by [Bastian Schmidt](https://github.com/batzen).

It allows you to spy/browse the visual, logical and automation tree of any running WPF application (without the need for a debugger).  
You can change property values, view triggers, set breakpoints on property changes and many more things.

[![Build status for master branch](https://img.shields.io/appveyor/ci/batzen/snoopwpf/master?style=flat-square&&label=upstream-master)](https://ci.appveyor.com/project/batzen/snoopwpf/branch/master)
[![Build status for develop branch](https://img.shields.io/appveyor/ci/batzen/snoopwpf/develop?style=flat-square&&label=upstream-develop)](https://ci.appveyor.com/project/batzen/snoopwpf/branch/develop)
[![Chocolatey version](http://img.shields.io/chocolatey/v/snoop.svg?style=flat-square)](https://chocolatey.org/packages/snoop)

[![Agent CI](https://github.com/InitialForce/snoopwpf/actions/workflows/agent-ci.yml/badge.svg?branch=develop)](https://github.com/InitialForce/snoopwpf/actions/workflows/agent-ci.yml)

Badges above reflect the upstream build. Fork CI runs via GitHub Actions on the `develop` branch of this repository.

## Why this fork exists

Modern software engineering increasingly leans on AI coding agents (Claude Code,
Claude Desktop, Cursor, etc.). These agents can read and write source code, run
commands, and debug programs — but they cannot *observe the running UI* of a WPF
application. Without runtime visibility, an agent debugging a WPF bug has to work
from source alone; it can't see the element tree, inspect bindings, verify property
values at runtime, or capture screenshots.

This fork adds that missing piece. The Snoop engine that powers the Snoop desktop
UI is exposed as an MCP server, so any MCP-compatible agent can drive tree
navigation, property inspection, binding diagnostics, and screenshots through a
typed tool surface — for the same app that a developer might be snooping
interactively in another window.

## Fork goals

- **AI-agent inspection** via 27 MCP tools covering visual / logical / automation
  trees, properties with binding and trigger detail, resources, behaviors,
  screenshots, and diagnostics.
- **Two deployment modes:**
  - *NuGet (compile-in)* — reference `SnoopWPF.Agent` and call
    `SnoopAgent.StartCoLocated()` at startup. Zero process-injection complexity. Intended
    for apps you own and can recompile.
  - *Injection (external host)* — `snoop-mcp.exe` injects into any running WPF
    process via the existing Snoop injection pipeline. For third-party or legacy
    apps.
- **UI coexistence.** The MCP agent and the classic Snoop desktop UI can run in
  the same process at the same time.
- **Secure by default.** Read-only unless mutation is explicitly enabled;
  mutations go through a hardcoded `TypeConverter` whitelist (never
  `TypeDescriptor.GetConverter`); named-pipe communication is `CurrentUserOnly`
  with a 256-bit session-token handshake and constant-time verification;
  properties whose names match a sensitive-keyword list (passwords, tokens, API
  keys, connection strings) are redacted on every read path, including trigger
  and behavior inspection.
- **Upstream compatibility.** The classic `Snoop` and `Snoop.Core` projects stay
  compatible with upstream. New functionality lives in new `SnoopWPF.Agent.*`
  projects so upstream merges stay clean. Non-AI bug fixes land here will be
  offered back upstream.

## Non-goals

- Replacing the classic Snoop desktop UI.
- Remote inspection over the network — pipes are localhost-only by design.
- Supporting AI protocols other than MCP.
- Self-contained single-file WPF apps on the injection path (same upstream limitation).

## MCP Agent quick start

**NuGet mode** (app you own):

```xml
<PackageReference Include="SnoopWPF.Agent" />
```

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    var agent = SnoopAgent.StartCoLocated();
    // agent.PipeName + agent.SessionToken when using Pipe transport
}
```

**Injection mode** (any running WPF process):

```json
// .mcp.json
{
  "mcpServers": {
    "snoop": { "command": "snoop-mcp", "args": ["--pid", "12345"] }
  }
}
```

## Documentation

- [MCP Agent Overview](docs/mcp-agent.md)
- [NuGet Mode](docs/nuget-mode.md)
- [Injection Mode](docs/injection-mode.md)
- [MCP Tools Reference](docs/mcp-tools-reference.md)
- [Security Model](docs/security.md)

## Design documents

Planning artifacts for this fork live at the repository root:

- [`PRD.md`](PRD.md) — product requirements document.
- [`BEADS.md`](BEADS.md) — implementation specification, 30 beads (atomic work units), with global security and build rules.
- [`TRANSFORMATION_PLAN.md`](TRANSFORMATION_PLAN.md) — rollout plan used to execute the beads.
- Earlier planning drafts are retained for history: `PRD-v4-automation.md`, `PRD-v5-MVP.md`, `PRD-v5-ideal.md`, `PRD-v5-sota-research.md`, `PRD-v5-reviews-wave2.md`, `PRD-v5-reviews-wave3.md`.

## Fork project layout

| Project | Purpose |
|---|---|
| `SnoopWPF.Agent.Contracts` | Shared DTOs, `ISnoopInspector` interface, protocol types. `net462;net6.0-windows;net8.0-windows`. |
| `SnoopWPF.Agent.Engine` | Core inspector implementation — tree, properties, bindings, diagnostics, resources, screenshots. Wraps `Snoop.Core`. |
| `SnoopWPF.Agent.Tools` | 27 MCP tool handlers (one per tool). |
| `SnoopWPF.Agent.Server` | NuGet-mode entry point: `SnoopAgent.StartCoLocated()`. Packs to `SnoopWPF.Agent` NuGet. |
| `SnoopWPF.Agent.Remote` | Host-side pipe client (`PipeSnoopInspectorProxy` implementing `ISnoopInspector`). |
| `SnoopWPF.Agent.Injection` | Injected-process DLL — hosts inspector, serves pipe, performs handshake. |
| `SnoopWPF.Agent.Host` | `snoop-mcp.exe` — injects into target PID, speaks MCP on stdio. |
| `SnoopWPF.Agent.Cli` | `snoop-cli.exe` — interactive CLI wrapper over the same engine. |
| `Snoop.Injector` | Extracted injection logic (previously embedded in the Snoop GUI project). |
| `Snoop.Core` / `Snoop` | Upstream projects, preserved. |
| `Samples/SnoopWPF.SampleApp` | Demonstrates NuGet-mode integration and provides a fixture for manual testing. |
| `SnoopWPF.Agent.Tests` / `.IntegrationTests` / `.InjectionTests` | Unit, WPF-dispatcher integration, and pipe-protocol tests respectively. |

---

## Contact

- [![Join the chat at https://gitter.im/snoopwpf/Lobby](https://img.shields.io/badge/GITTER-join%20chat-green.svg?style=flat-square)](https://gitter.im/snoopwpf/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
- [![Twitter](https://img.shields.io/badge/twitter-%40batzendev-blue.svg?style=flat-square)](https://twitter.com/batzendev)

## Where can i download Snoop?/How can i install Snoop?

- [Chocolatey](https://chocolatey.org/packages/snoop) for stable and some preview versions
- [GitHub releases](https://github.com/snoopwpf/snoopwpf/releases) for stable versions
- [AppVeyor](https://ci.appveyor.com/project/batzen/snoopwpf/build/artifacts) for the latest preview versions (built on every code change)
- You need at least .NET Framework 4.6.2 to run Snoop

## Supported .NET versions

- .NET Framework >= 4.6.2
- .NET >= 6
  - Tested with 6, 7, 8, 9 and 10. Future versions might just work.
  - **Restrictions:** Self-Contained single file applications are not supported as there is no reliable way to get a handle to the .NET runtime

## Versions

You can read the [changelog](Changelog.md) for details on what changed in which version.

### [6.0.0](../../releases/tag/v6.0.0)

Breaking:

- Dropped support for all .NET Framework versions prior to .NET 4.6.2
- Dropped support for .NET 3.1 and NET 5

### [5.0.0](../../releases/tag/v5.0.0)

Highlights:

- Improved settings system that does not rely on `System.Configuration`  
  The new system allows sharing of settings between different snooped applications.  
  It also allows to define settings for whole directory trees.
- It's now possible to hide properties from Snoop in it's default view.  
  Just annotate your properties with `[System.ComponentModel.BrowsableAttribute(false)]`.
- Added the ability to show browser dev tools on browser controls.  
  `WebView2` and `CefSharp` are currently supported.
- Added dark theme

Breaking:

- Dropped support for all .NET Framework versions prior to .NET 4.5.2
- Dropped support for .NET 3.0
- Added support for .NET versions >= 6.0 (by not explicitly blocking versions greater than 6.0)

### [4.0.0](../../releases/tag/v4.0.0)

Highlights:

- Support for .NET 6.0
- Support for ARM/ARM64
- New "Diagnostics" view
- Settings for highlighting
- Artifacts are digitally signed thanks to SignPath.io (MSI, Chocolatey NUPKG and zip)

Breaking:

- Dropped support for all .NET versions prior to .NET 4.5.1

### [3.0.0](../../releases/tag/v3.0.0)

Highlights:

- Support for .NET Core (3.0, 3.1 and 5.0) (including self contained and single file applications)
- Rewritten injector code
- You no longer have to have installed any Microsoft Visual C++ Redistributable(s)
- Snooping disabled controls when holding `CTRL + SHIFT` works now
- Snoop now filters uncommon properties by default
- Snoop is now able to show `MergedDictionaries` from `ResourceDictionary`
- Snoop now has two tracking modes.
  - Holding `CTRL` tries to skip template parts => this is changed to `CTRL + ALT` in newer versions
  - Holding `CTRL + SHIFT` does not skip template parts
- Drastically improved performance of `AppChooser.Refresh()` (thanks @mikel785)
- Usability improvements for process dropdown (thanks @mikel785)
- Support for displaying the logical tree and the tree of WPF automation peers
- Ability to inspect `Popup` without opening it
- `Snoop.exe` and the injector launcher now support commandline args
- Global hotkey support (just start snoop, focus a WPF application and hit `CTRL + WIN + ALT + F12`)

Known issues:

- Trying to snoop a trimmed single file application might not work as trimming might have removed things Snoop relies on

### [2.11.0](../../releases/tag/2.11.0)

Highlights:

- Support for multiple app domains
- Auto elevation to enable spying of elevated processes without running Snoop as administrator
- Persistent settings for various settings
- Improved error dialog and issue reporting
- Rewritten window finder

### [2.10.0](../../releases/tag/2.10.0)

Was released on September 19th, 2018.
In this version we finally got rid of support for snooping WPF 3.5 applications.
This allowed us to move the Snoop projects forward to Visual Studio 2017 which should make it much easier to work with Snoop's source code.

### [2.9.0](../../releases/tag/2.9.0)

Was released on July 27th, 2018.
The big addition in this version was the inclusion of the triggers tab which was a useful feature of another WPF spying utility called WPF Inspector (written by [Christan Moser](https://github.com/ChristianMoser)).
It was ported to Snoop by Bastian Schmidt.

## Documentation on how to use Snoop

Unfortunately there isn't any exhaustive documentation on how to use Snoop and there are plenty of hidden features. If someone is willing to work on this, please let me know. On the bright side, it is a pretty easy utility to use and learn. I have made three videos which should get most people quick started.

Here are the links to the current Snoop Tips & Tricks:

- https://www.youtube.com/watch?v=n8EdRR0Tc1k
- https://www.youtube.com/watch?v=98UEVCQHmVA
- https://www.youtube.com/watch?v=frXAgGzZnrU

## Why can't I snoop my application?

Well, you can! You will just need to use an earlier version of Snoop, in order to do so.  
The minimum versions are:

| Snoop | .NET Framework | .NET |
|-------|----------------|------|
| 3.0   | 4.0            | 3.0  |
| 4.0   | 4.5.1          | 3.0  |
| 5.0   | 4.5.2          | 3.1  |
| 6.0   | 4.6.2          | 6.0  |

## How do i build Snoop?

Just open `Snoop.sln` with Visual Studio and build it.

Requirements:

- Visual Studio 2022 or later
  - C++ payloads (x86/x64 and optionally ARM/ARM64)
  - You can import the [.vsconfig](.vsconfig) file in the Visual Studio installer to let it install all required components
- .NET SDK 10.0.100 or later

## Contributors

Over time contributions have been added by several people, most notably:

- [Bastian Schmidt](https://github.com/batzen), [batzen.dev](https://batzen.dev) (current maintainer)
- [Cory Plotts](https://github.com/cplotts)
- [Dan Hanan](http://blogs.interknowlogy.com/author/danhanan/)
- [Andrei Kashcha](http://blog.yasiv.com/)
- [Maciek Rakowski](https://github.com/MaciekRakowski)
- [Bailey Ling](https://github.com/bling)

## Code Signing

Snoop uses free code signing provided by [SignPath.io](https://signpath.io?utm_source=foundation&utm_medium=github&utm_campaign=snoopwpf) and a free code signing certificate by the [SignPath Foundation](https://signpath.org?utm_source=foundation&utm_medium=github&utm_campaign=snoopwpf)
