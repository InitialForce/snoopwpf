# SnoopWPF.Agent.Contracts

Shared DTOs, interfaces, and WpfLocator for SnoopWPF Agent. Referenced by both the
server (injected side) and remote/broker clients.

**Targets: .NET 4.6.2, .NET 6.0-windows, .NET 8.0-windows**

## Contents

- `SnoopAgentOptions` — configuration options for `SnoopAgent.StartCoLocated()` and related entry points
- `TransportMode` — enum: `Stdio` or `Pipe`
- `ISnoopInspector` — interface implemented by the agent engine
- `WpfLocator` / `WpfLocatorParser` — element-finding DSL
- DTOs: `ElementDto`, `PropertyDto`, `BindingInfoDto`, `WindowDto`, and more
- `Protocol` types: `BrokerHandshakePayload`, `HandshakeChallenge`, `HandshakeResponse`
- `SnoopException` / `SnoopErrorCode` — structured error types

## Notes

This package is an implementation detail of `SnoopWPF.Agent` and `SnoopWPF.Agent.BrokerHost`.
Most consumers do not need to reference it directly — it is bundled into those packages.
Add a direct reference only if you are implementing a custom transport or custom tools.

## Documentation

- [SnoopWPF.Agent](../SnoopWPF.Agent.Server/README.md) — main consumer-facing package
- [MCP Tools Reference](../docs/mcp-tools-reference.md)
