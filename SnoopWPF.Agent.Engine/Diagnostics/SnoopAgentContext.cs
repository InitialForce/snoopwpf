namespace SnoopWPF.Agent.Engine.Diagnostics;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Provides AsyncLocal-scoped per-tool-call diagnostics accumulation.
/// </summary>
/// <remarks>
/// <para>
/// Each MCP tool invocation should begin a scope via <see cref="BeginScope"/> before
/// dispatching to the handler, and drain accumulated warnings via <see cref="DrainWarnings"/>
/// after the handler returns.  This ensures warnings from one tool call do not leak into
/// subsequent calls and that concurrent tool invocations each see their own isolated list.
/// </para>
/// <para>
/// Handler code (or any helper it calls) accumulates non-fatal diagnostics via
/// <see cref="AddWarning(string,string,object?)"/>.  The caller (dispatcher) reads them
/// back with <see cref="DrainWarnings"/> and attaches them to the response envelope.
/// </para>
/// <para>
/// Empty-warnings behaviour: when the handler emits no warnings, <see cref="DrainWarnings"/>
/// returns an empty list.  The downstream serialiser omits <c>warnings</c> from the JSON
/// envelope when the list is empty (consistent-empty = omitted).
/// </para>
/// </remarks>
public static class SnoopAgentContext
{
    // AsyncLocal stores a reference to the per-scope list.
    // A new list is assigned on each BeginScope() call, replacing the reference in the
    // current execution context without affecting parent or sibling contexts.
    private static readonly AsyncLocal<List<AgentWarning>?> CurrentWarnings = new();

    /// <summary>
    /// Starts a new diagnostic scope for the current async call chain.
    /// Any warnings accumulated in a previous scope for this context are discarded.
    /// </summary>
    /// <returns>
    /// An <see cref="IDisposable"/> that clears the scope on disposal.
    /// Disposing the scope is optional but recommended to avoid stale references in
    /// thread-pool threads that are returned to the pool after use.
    /// </returns>
    public static IDisposable BeginScope()
    {
        CurrentWarnings.Value = new List<AgentWarning>();
        return new ScopeHandle();
    }

    /// <summary>
    /// Appends a structured warning to the current tool-call scope.
    /// No-op when called outside a scope (no <see cref="BeginScope"/> has been called).
    /// </summary>
    /// <param name="code">Short machine-readable code, e.g. <c>"ELEMENT_NOT_VISIBLE"</c>.</param>
    /// <param name="message">Human-readable explanation for LLM consumption.</param>
    /// <param name="data">Optional structured data attached to the warning (serialised as-is).</param>
    public static void AddWarning(string code, string message, object? data = null)
    {
        var list = CurrentWarnings.Value;
        if (list is null)
        {
            return;
        }

        list.Add(new AgentWarning(code, message, data, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Returns all warnings accumulated in the current scope (in insertion order) and
    /// clears the scope list, preventing double-drain.
    /// </summary>
    /// <returns>
    /// A snapshot of accumulated <see cref="AgentWarning"/> items; empty when none were added
    /// or when called outside a scope.
    /// </returns>
    public static IReadOnlyList<AgentWarning> DrainWarnings()
    {
        var list = CurrentWarnings.Value;
        if (list is null || list.Count == 0)
        {
            return Array.Empty<AgentWarning>();
        }

        // Snapshot and clear so a second drain returns empty.
        var snapshot = list.ToArray();
        list.Clear();
        return snapshot;
    }

    // -------------------------------------------------------------------------

    private sealed class ScopeHandle : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            CurrentWarnings.Value = null;
        }
    }
}

/// <summary>
/// An immutable diagnostic warning accumulated during a single tool invocation.
/// </summary>
/// <param name="Code">Short machine-readable code identifying the warning class.</param>
/// <param name="Message">Human-readable explanation suitable for LLM consumption.</param>
/// <param name="Data">Optional structured payload; may be <see langword="null"/>.</param>
/// <param name="Timestamp">UTC timestamp when the warning was recorded.</param>
public sealed record AgentWarning(
    string Code,
    string Message,
    object? Data,
    DateTimeOffset Timestamp);
