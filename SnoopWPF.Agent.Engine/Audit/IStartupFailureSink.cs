// SnoopWPF.Agent.Engine/Audit/IStartupFailureSink.cs
namespace SnoopWPF.Agent.Engine.Audit;

using System;

/// <summary>
/// Receives startup-failure notifications so that callers can surface them without
/// crashing the host process.
/// </summary>
/// <remarks>
/// Implemented by <c>SnoopAgentHandle</c> (server layer). The engine keeps a reference
/// so that <c>RunServerAsync</c> / <c>RunBrokeredAsync</c> can report fatal startup
/// errors back to the embedding application and to the MCP client via stderr.
/// </remarks>
public interface IStartupFailureSink
{
    /// <summary>
    /// Called once when the server startup task faults with an unhandled exception.
    /// Implementations must be idempotent and must not throw.
    /// </summary>
    /// <param name="ex">The exception that caused startup to fail.</param>
    void OnStartupFailed(Exception ex);
}
