namespace SnoopWPF.Agent.BrokerHost;

using System;

/// <summary>
/// Configuration options for <see cref="BrokerHost"/>.
/// </summary>
public sealed class BrokerOptions
{
    /// <summary>
    /// Gets or sets the name of the named pipe used to connect to the current target process.
    /// Must be set before calling <see cref="BrokerHost.Start"/>.
    /// </summary>
    public string PipeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the callback invoked when the target process disconnects from the broker pipe.
    /// External lifecycle code (e.g. MC's <c>UiMcpHost</c>) uses this hook to surface
    /// <c>TARGET_NOT_RUNNING</c> failures to the MCP caller.
    /// May be <see langword="null"/> if the consumer does not need disconnection notifications.
    /// </summary>
    public Action? OnTargetDisconnected { get; set; }
}
