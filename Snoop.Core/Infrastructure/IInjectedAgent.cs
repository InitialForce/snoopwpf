namespace Snoop.Infrastructure;

using System;
using Snoop.Data;

/// <summary>
/// Abstraction for a headless agent that can run in-process alongside or instead of the Snoop UI.
/// In NuGet mode, set <see cref="SnoopManager.HeadlessAgentFactory"/> before calling into SnoopManager.
/// In injection mode, the agent entry point creates <see cref="SnoopInspector"/> directly and bypasses SnoopManager.
/// </summary>
public interface IInjectedAgent : IDisposable
{
    /// <summary>Starts the agent with the given settings.</summary>
    void Start(TransientSettingsData settings);

    /// <summary>Stops the agent and releases resources.</summary>
    void Stop();
}
