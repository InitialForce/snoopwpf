// Types moved to SnoopWPF.Agent.Contracts so that SessionPolicy.Create can reference them.
// Re-export via global aliases so all code in this assembly continues to compile unchanged.
#pragma warning disable SA1200 // global using must be file-scoped
global using SnoopAgentOptions = SnoopWPF.Agent.Contracts.SnoopAgentOptions;
global using TransportMode = SnoopWPF.Agent.Contracts.TransportMode;
#pragma warning restore SA1200
