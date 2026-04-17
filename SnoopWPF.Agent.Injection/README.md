# SnoopWPF.Agent.Injection

Named-pipe agent entry point for SnoopWPF. Provides the `PipeAgentServer` that
runs inside the injected WPF process and serves inspector requests over a named pipe.

**Targets: .NET 4.6.2, .NET 6.0-windows, .NET 8.0-windows**
**Not intended for direct consumption — bundled into `SnoopWPF.Agent`.**

## Notes

This package is an internal implementation detail. It is bundled into the
`SnoopWPF.Agent` NuGet package. Consumers should reference `SnoopWPF.Agent` directly.

For injection-mode usage (third-party WPF apps), use `snoop-mcp.exe`:

```bash
snoop-mcp.exe --attach-pid <PID>
```

## Documentation

- [Injection mode guide](../docs/injection-mode.md)
- [Security Model](../docs/security.md)
