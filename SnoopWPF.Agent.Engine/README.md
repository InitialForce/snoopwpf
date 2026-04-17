# SnoopWPF.Agent.Engine

Internal WPF inspection engine for SnoopWPF Agent. Provides the `SnoopInspector`
implementation that walks the visual tree, reads properties, captures screenshots,
and tracks state deltas.

**Targets: .NET 4.6.2, .NET 6.0-windows, .NET 8.0-windows**
**Not intended for direct consumption — bundled into `SnoopWPF.Agent`.**

## Notes

This package is an internal implementation detail. It is bundled into the
`SnoopWPF.Agent` NuGet package rather than listed as a dependency. Consumers
should reference `SnoopWPF.Agent` or `SnoopWPF.Agent.BrokerHost` instead.

## Documentation

- [SnoopWPF.Agent](../SnoopWPF.Agent.Server/README.md) — main consumer-facing package
- [Security Model](../docs/security.md)
