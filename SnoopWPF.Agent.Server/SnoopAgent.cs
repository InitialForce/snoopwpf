namespace SnoopWPF.Agent.Server;

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Public entry point for embedding the SnoopWPF MCP server in a WPF application.
/// </summary>
/// <remarks>
/// Call <see cref="StartCoLocated"/> once from the WPF UI thread after the application is initialized.
/// The returned <see cref="SnoopAgentHandle"/> can be disposed to stop the server.
/// The server also stops automatically when the hosting <see cref="Application"/> exits.
/// </remarks>
public static class SnoopAgent
{
    private static volatile SnoopAgentHandle? activeHandle;
    private static readonly object Lock = new();

    /// <summary>
    /// Starts the SnoopWPF MCP server in co-located mode (embedded in the target WPF process).
    /// </summary>
    /// <param name="options">Optional configuration. Defaults to stdio transport.</param>
    /// <returns>
    /// A <see cref="SnoopAgentHandle"/> that can be disposed to stop the server.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a server is already running (only one instance is allowed).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if called from a thread without a WPF <see cref="Dispatcher"/>.
    /// </exception>
    public static SnoopAgentHandle StartCoLocated(SnoopAgentOptions? options = null)
    {
        options ??= new SnoopAgentOptions();

        lock (Lock)
        {
            if (activeHandle != null)
            {
                throw new InvalidOperationException(
                    "SnoopAgent is already running. Dispose the existing handle before calling StartCoLocated() again.");
            }

            var dispatcher = Dispatcher.CurrentDispatcher
                ?? throw new InvalidOperationException(
                    "SnoopAgent.StartCoLocated() must be called from a thread that has a WPF Dispatcher.");

            // Construct the immutable session policy once at session start (S1).
            var policy = SessionPolicy.Create(SessionMode.CoLocated, options);

            var inspectorOptions = new SnoopInspectorOptions
            {
                TimeoutMs = options.TimeoutMs,
                EnableMutation = policy.EnableMutation,
                EnableRedaction = policy.EnableRedaction,
            };

            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: Application.Current,
                options: inspectorOptions);

            var cts = new CancellationTokenSource();
            var handle = new SnoopAgentHandle(cts, inspector, policy);
            activeHandle = handle;

            // For pipe transport: resolve/generate pipe name and session token now, before
            // handing off to the background task, so the caller can read them immediately.
            if (options.Transport == TransportMode.Pipe)
            {
                handle.PipeName = !string.IsNullOrEmpty(options.PipeName)
                    ? options.PipeName
                    : "snoop-agent-" + Guid.NewGuid().ToString("N");

                handle.SessionToken = !string.IsNullOrEmpty(options.SessionToken)
                    ? options.SessionToken
                    : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }

            // Auto-stop when the application exits.
            var app = Application.Current;
            if (app != null)
            {
                app.Exit += (_, _) => handle.Dispose();
            }

            // Run the server on the thread pool so we don't block the caller or the Dispatcher.
            _ = Task.Run(
                () => RunServerAsync(inspector, options, policy, handle, cts.Token),
                cts.Token);

            Trace.TraceInformation("SnoopWPF.Agent MCP server starting ({0} transport).", options.Transport);
            return handle;
        }
    }

    /// <summary>
    /// Starts the SnoopWPF MCP server.
    /// </summary>
    /// <param name="options">Optional configuration. Defaults to stdio transport.</param>
    /// <returns>A <see cref="SnoopAgentHandle"/> that can be disposed to stop the server.</returns>
    [Obsolete("Use StartCoLocated. This overload will be removed in v2.0.")]
    public static SnoopAgentHandle Start(SnoopAgentOptions? options = null)
        => StartCoLocated(options);

    /// <summary>Called by <see cref="SnoopAgentHandle.Dispose"/> to clear the active handle.</summary>
    internal static void ClearHandle()
    {
        lock (Lock)
        {
            activeHandle = null;
        }
    }

    private static async Task RunServerAsync(
        SnoopInspector inspector,
        SnoopAgentOptions options,
        SessionPolicy policy,
        SnoopAgentHandle handle,
        CancellationToken ct)
    {
        try
        {
            await McpServerSetup.RunServerAsync(inspector, options, policy, handle, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — swallow.
        }
        catch (Exception ex)
        {
            // Surface unexpected errors without crashing the host process.
            Trace.TraceError("SnoopWPF.Agent MCP server error: {0}", ex.Message);
        }
    }
}
