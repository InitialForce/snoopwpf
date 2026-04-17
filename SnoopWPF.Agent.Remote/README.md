# SnoopWPF.Agent.Remote

Pipe client and framing library for SnoopWPF Agent brokered mode. Provides the
`FramedJsonTransport` and broker-side client types used to communicate with an
injected `SnoopWPF.Agent` over a named pipe.

**Targets: .NET 8.0-windows**

## When to Use

`SnoopWPF.Agent.Remote` is consumed by `SnoopWPF.Agent.BrokerHost` internally.
Most consumers should reference `SnoopWPF.Agent.BrokerHost` directly, which
bundles this package's DLL automatically.

Add a direct reference only if you are building a custom broker transport layer
and need the low-level framing primitives.

## Documentation

- [Brokered-mode integration guide](../docs/brokered-mode-integration.md)
- [SnoopWPF.Agent.BrokerHost](../SnoopWPF.Agent.BrokerHost/README.md)
