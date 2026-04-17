# Brokered-Mode Integration Guide

Brokered mode decouples the MCP stdio anchor (long-lived broker process) from the
SnoopWPF agent (ephemeral, runs inside the target WPF process). This enables consumers
such as MC's `UiMcpHost` to spawn fresh target instances per test scenario without
losing the MCP connection.

## Architecture

```
MCP client (Claude Code / Cursor)
        | stdio
        v
 [Broker process]  <--named pipe-->  [Target WPF process]
 SnoopWPF.Agent.BrokerHost              SnoopAgent.StartBrokered()
 SnoopWPF.Agent.Remote                  SnoopWPF.Agent.Server
```

The broker owns the MCP stdio channel. The target connects to the broker over a named
pipe. Broker and target are separate OS processes.

## Target-side integration (Program.cs patch)

Add to the WPF application's entry point. The broker passes `--snoop-pipe=NAME` on the
command line and delivers the session token via a single-line JSON payload written to the
target's stdin immediately after spawn (`BrokerHandshakePayload { Pipe, Token }`). The token
never appears on the process command line.

```csharp
// In App.xaml.cs or application startup:
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    string? pipeName = GetFlagValue(e.Args, "--snoop-pipe");

    if (!string.IsNullOrEmpty(pipeName))
    {
        // Secure path: broker writes a single-line JSON BrokerHandshakePayload to stdin.
        // Read it synchronously during startup before the WPF message pump starts.
        string? line = Console.In.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            // No handshake payload — broker did not write one. Skip brokered mode.
            return;
        }

        var payload = System.Text.Json.JsonSerializer
            .Deserialize<SnoopWPF.Agent.Contracts.Protocol.BrokerHandshakePayload>(line);

        if (payload is null || string.IsNullOrEmpty(payload.Token))
            return; // Malformed payload — skip brokered mode.

        // Use the pipe name from the payload if provided; otherwise use the command-line one.
        if (!string.IsNullOrEmpty(payload.Pipe))
            pipeName = payload.Pipe;

        string token = payload.Token;

        // Brokered mode: agent connects back to broker over the named pipe.
        // Console.Out is NOT redirected — the broker drains the target's stdout.
        _agentHandle = SnoopAgent.StartBrokered(
            this,
            pipeName,
            token,
            new SnoopAgentOptions
            {
                EnableMutation = true,
                EnableRedaction = false,
            });
    }
    // else: co-located or injection mode
}

// Argument parser helper:
private static string? GetFlagValue(string[] args, string prefix)
{
    foreach (string arg in args)
        if (arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
            return arg.Substring(prefix.Length + 1);
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}
```

### What `StartBrokered` does

- Opens a `NamedPipeServerStream(pipeName, CurrentUserOnly)` and waits for the broker to connect.
- Performs the framed-JSON handshake (challenge/response with session token).
- The session token is delivered exclusively via stdin (`BrokerHandshakePayload`) — it never
  appears on the process command line.
- Runs a reconnect loop so the broker can restart without requiring a target restart.
- Does **not** redirect `Console.Out` — the target's stdout is owned by the target, not the MCP transport.

## Broker-side skeleton (external consumer reference)

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using SnoopWPF.Agent.BrokerHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// MCP 1.2.0+: StdioServerTransport takes McpServerOptions (not zero-arg).

// 1. Generate pipe name + token (broker decides these values).
string pipeName = "my-app-" + Guid.NewGuid().ToString("N")[..8];
string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
using var cts = new CancellationTokenSource();

// 2. FIRST statement: silence stdout. Broker owns MCP stdio.
Console.SetOut(TextWriter.Null);

// 3. Spawn the target with --snoop-pipe=NAME only.
//    BrokerTargetSpawner writes the BrokerHandshakePayload (pipe + token) to the child's
//    stdin automatically, then drains target stdout/stderr so nothing leaks to broker's stdio.
//    The token is NEVER placed on the command line.
var process = BrokerTargetSpawner.Spawn(
    exe: @"C:\path\to\MyApp.exe",
    args: Array.Empty<string>(), // additional app args (use IReadOnlyList<string> overload)
    pipeName: pipeName,
    tokenHex: tokenHex);

// 4. Run the broker MCP server on stdio.
var opts = new BrokerOptions
{
    PipeName = pipeName,
    SessionToken = tokenHex,   // required (FX2-C3): must match token delivered via stdin
    OnTargetDisconnected = () =>
    {
        // Optionally restart the target or surface TARGET_NOT_RUNNING to MCP callers.
        cts.Cancel();
    },
};

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "my-broker", Version = "1.0.0" },
};
await using var transport = new StdioServerTransport(serverOptions); // MCP 1.2.0 API
await BrokerHost.Start(transport, opts, cts.Token);

// 5. On shutdown: kill the target (sample_exit).
if (!process.HasExited)
    process.Kill();
```

## Lifecycle tools

MC's `UiMcpHost` registers additional lifecycle tools on top of the 18-tool core surface:

| Tool | Description |
|------|-------------|
| `mc_launch` / `sample_launch` | Spawn the target process (calls `BrokerTargetSpawner.Spawn`). |
| `mc_exit` / `sample_exit` | Kill the target process. |
| `mc_restart` | Kill + re-spawn (preserves broker/MCP connection). |
| `mc_attach_pid` | Attach to an already-running target by PID (injection fallback). |

These are **not** part of `SnoopWPF.Agent.BrokerHost` — they are consumer-specific and
live in the consuming application (`UiMcpHost`, `SnoopWPF.SampleBroker`, etc.).

## Injection-mode brokered quickstart

Use this path to inspect a third-party WPF app you cannot modify. The `snoop-mcp.exe` broker
injects the agent DLL into the target process automatically.

```bash
# 1. Launch your WPF target app normally (or it may already be running).
#    Note its PID, e.g. 5432.

# 2. Start the broker in injection mode, pointing at the running PID.
snoop-mcp.exe --attach-pid 5432
```

The broker:
1. Generates a pipe name and session token.
2. Injects `SnoopWPF.Agent.dll` into the target process via `snoop-mcp.exe --inject`.
3. Writes the `BrokerHandshakePayload` to the injected agent's stdin substitute.
4. Starts the MCP stdio server — ready for Claude Code or Cursor to connect.

Connect your MCP client to the broker's stdio. All 28 `wpf_*` tools are available. When the
broker exits, the injected agent is unloaded automatically.

> **Note:** Injection requires the same Windows user and is subject to DEP/CFG constraints.
> For best results, prefer the target-side integration (compile-time) when you own the source.

---

## Pipe security

- Pipe ACL: `PipeOptions.CurrentUserOnly` — only the same Windows user can connect.
- Session token: 256-bit random hex string, verified via constant-time comparison.
- Pipe name: generated by the broker; passed to the target via `--snoop-pipe=NAME`.
- `maxAllowedInstances=1`: single broker per target (MVP constraint).

## Known limitations (MVP)

- Single broker per target (`maxAllowedInstances=1`). Two brokers against one target will queue.
- No broker-target authentication beyond current-user ACL + session token.
- Multi-user or cross-elevation scenarios are not supported in MVP.

## Sample projects

| Project | Role |
|---------|------|
| `Samples/SnoopWPF.SampleApp` | Target WPF application. Demonstrates `--mcp-stdio` (co-located) and `--snoop-pipe` + `--snoop-token` (brokered). Pass `--smoke` to self-test and exit. |
| `Samples/SnoopWPF.SampleBroker` | Minimal broker. Spawns SampleApp as a target over named pipe, runs MCP server on stdio. Pass `--smoke` to validate broker infrastructure. |

## NuGet packages

Consume from the GitHub Packages feed (M2-18):

```xml
<PackageReference Include="SnoopWPF.Agent" Version="6.0.0-*" />            <!-- target side -->
<PackageReference Include="SnoopWPF.Agent.BrokerHost" Version="6.0.0-*" /> <!-- broker side -->
<PackageReference Include="SnoopWPF.Agent.Remote" Version="6.0.0-*" />     <!-- broker side -->
```
