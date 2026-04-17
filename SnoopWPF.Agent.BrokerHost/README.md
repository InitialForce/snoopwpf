# SnoopWPF.Agent.BrokerHost

Brokered-mode host library for SnoopWPF Agent. Provides the MCP broker infrastructure
consumed by external host processes to communicate with injected WPF agents over a
named pipe.

**Requires .NET 8.0+.**

## When to Use

Use `SnoopWPF.Agent.BrokerHost` when you need to separate the MCP stdio anchor (the
broker process) from the WPF target process. This is ideal for:

- Testing frameworks that spawn fresh WPF app instances per test scenario
- Third-party app inspection (injection mode via `snoop-mcp.exe`)
- Host applications that must survive WPF target crashes/restarts

For simple co-located embedding (agent inside your own WPF app), use
[SnoopWPF.Agent](https://www.nuget.org/packages/SnoopWPF.Agent) instead.

## Quick Start

```csharp
using SnoopWPF.Agent.BrokerHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Cryptography;

// 1. Generate pipe name + token.
string pipeName = "my-app-" + Guid.NewGuid().ToString("N")[..8];
string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

// 2. FIRST statement: silence stdout. Broker owns MCP stdio.
Console.SetOut(TextWriter.Null);

// 3. Spawn the target process with the named pipe.
//    BrokerTargetSpawner writes BrokerHandshakePayload to the child's stdin automatically.
var process = BrokerTargetSpawner.Spawn(
    exe: @"C:\path\to\MyApp.exe",
    args: Array.Empty<string>(),
    pipeName: pipeName,
    tokenHex: tokenHex);

// 4. Run the MCP broker server on stdio.
using var cts = new CancellationTokenSource();
var opts = new BrokerOptions
{
    PipeName = pipeName,
    SessionToken = tokenHex,
    OnTargetDisconnected = () => cts.Cancel(),
};

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "my-broker", Version = "1.0.0" },
};
await using var transport = new StdioServerTransport(serverOptions);
await BrokerHost.Start(transport, opts, cts.Token);
```

## Architecture

```
MCP client (Claude Code / Claude Desktop)
        | stdio
        v
 [Broker process]  <--named pipe-->  [Target WPF process]
 SnoopWPF.Agent.BrokerHost              SnoopAgent.StartBrokered()
```

## Documentation

- [Brokered-mode integration guide](../docs/brokered-mode-integration.md)
- [Security Model](../docs/security.md)
