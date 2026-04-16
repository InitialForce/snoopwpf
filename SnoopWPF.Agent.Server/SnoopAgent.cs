namespace SnoopWPF.Agent.Server;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SnoopWPF.Agent.Engine;

/// <summary>
/// Public entry point for embedding the SnoopWPF MCP server in a WPF application.
/// </summary>
/// <remarks>
/// Call <see cref="Start"/> once from the WPF UI thread after the application is initialized.
/// The returned <see cref="SnoopAgentHandle"/> can be disposed to stop the server.
/// The server also stops automatically when the hosting <see cref="Application"/> exits.
/// </remarks>
public static class SnoopAgent
{
    private static volatile SnoopAgentHandle? activeHandle;
    private static readonly object Lock = new();

    /// <summary>
    /// Starts the SnoopWPF MCP server.
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
    public static SnoopAgentHandle Start(SnoopAgentOptions? options = null)
    {
        options ??= new SnoopAgentOptions();

        lock (Lock)
        {
            if (activeHandle != null)
            {
                throw new InvalidOperationException(
                    "SnoopAgent is already running. Dispose the existing handle before calling Start() again.");
            }

            var dispatcher = Dispatcher.CurrentDispatcher
                ?? throw new InvalidOperationException(
                    "SnoopAgent.Start() must be called from a thread that has a WPF Dispatcher.");

            var inspectorOptions = new SnoopInspectorOptions
            {
                TimeoutMs = options.TimeoutMs,
                EnableMutation = options.EnableMutation,
                EnableRedaction = options.EnableRedaction,
            };

            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: Application.Current,
                options: inspectorOptions);

            var cts = new CancellationTokenSource();
            var handle = new SnoopAgentHandle(cts, inspector);
            activeHandle = handle;

            // Auto-stop when the application exits.
            var app = Application.Current;
            if (app != null)
            {
                app.Exit += (_, _) => handle.Dispose();
            }

            // Run the server on the thread pool so we don't block the caller or the Dispatcher.
            _ = Task.Run(
                () => RunServerAsync(inspector, options, cts.Token),
                cts.Token);

            Console.WriteLine($"SnoopWPF.Agent MCP server starting ({options.Transport} transport).");
            return handle;
        }
    }

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
        CancellationToken ct)
    {
        try
        {
            await McpServerSetup.RunServerAsync(inspector, options, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — swallow.
        }
        catch (Exception ex)
        {
            // Surface unexpected errors without crashing the host process.
            Console.Error.WriteLine($"SnoopWPF.Agent MCP server error: {ex.Message}");
        }
    }
}
