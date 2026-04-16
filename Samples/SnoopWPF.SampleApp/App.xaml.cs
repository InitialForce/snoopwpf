namespace SnoopWPF.SampleApp;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Server;

/// <summary>
/// Entry point for the SnoopWPF Sample Application.
/// Demonstrates NuGet-mode MCP agent integration.
///
/// Launch paths:
///   (default / --mcp-stdio)    Co-located mode: MCP server on stdio transport.
///   --snoop-pipe=NAME           Brokered mode (secure): broker writes a single-line JSON
///                               handshake payload to stdin containing pipe name and token.
///   --snoop-pipe=NAME --snoop-token=HEX  Brokered mode (legacy, deprecated): token passed
///                               on the command line. Emits a deprecation warning to stderr.
///   --smoke                     Self-test: start agent, call wpf_get_session_info,
///                               assert windows.Count >= 1, exit 0 (non-zero on failure).
/// </summary>
public partial class App : Application
{
    private SnoopAgentHandle? agentHandle;
    private HiddenWindow? hiddenWindow;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string[] args = e.Args;

        bool noAgent = HasFlag(args, "--no-agent");
        bool smoke = HasFlag(args, "--smoke");

        string? pipeName = GetFlagValue(args, "--snoop-pipe");
        string? tokenFromArgs = GetFlagValue(args, "--snoop-token");

        // Resolve the token and pipe name for brokered mode.
        string? token = null;

        if (!string.IsNullOrEmpty(pipeName))
        {
            if (!string.IsNullOrEmpty(tokenFromArgs))
            {
                // Legacy path: token was passed on the command line.
                // Emit a deprecation warning so operators know to upgrade.
                Console.Error.WriteLine(
                    "[SnoopWPF.SampleApp] WARNING: --snoop-token on the command line is deprecated " +
                    "and will be removed in a future release. Upgrade the broker to use the stdin " +
                    "handshake (BrokerHandshakePayload) so the session token is not exposed on the " +
                    "process command line.");
                token = tokenFromArgs;
            }
            else
            {
                // Secure path: broker writes a single-line JSON handshake payload to our stdin.
                // Read it synchronously during startup before the WPF message pump starts.
                string? line = Console.In.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.Error.WriteLine(
                        "[SnoopWPF.SampleApp] ERROR: --snoop-pipe was set but stdin contained no " +
                        "handshake payload. Expected a single-line JSON BrokerHandshakePayload.");
                    Application.Current.Shutdown(3);
                    return;
                }

                BrokerHandshakePayload? payload = null;
                try
                {
                    payload = JsonSerializer.Deserialize<BrokerHandshakePayload>(line);
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine(
                        $"[SnoopWPF.SampleApp] ERROR: Failed to parse handshake payload from stdin: {ex.Message}");
                    Application.Current.Shutdown(3);
                    return;
                }

                if (payload is null || string.IsNullOrEmpty(payload.Token))
                {
                    Console.Error.WriteLine(
                        "[SnoopWPF.SampleApp] ERROR: Handshake payload from stdin was null or missing token.");
                    Application.Current.Shutdown(3);
                    return;
                }

                // Use the pipe name from the payload if provided; otherwise keep the command-line one.
                if (!string.IsNullOrEmpty(payload.Pipe))
                {
                    pipeName = payload.Pipe;
                }

                token = payload.Token;
            }
        }

        // Brokered mode: both pipe name and token must be resolved.
        bool brokeredMode = !string.IsNullOrEmpty(pipeName) && !string.IsNullOrEmpty(token);

        // Keep a hidden window open for the lifetime of the application.
        this.hiddenWindow = new HiddenWindow();
        this.hiddenWindow.Show();
        this.hiddenWindow.Hide();

        if (!noAgent)
        {
            if (brokeredMode)
            {
                // Brokered mode: target-side. Broker owns the MCP stdio channel.
                // Console.Out is NOT redirected here — broker drains it.
                this.agentHandle = SnoopAgent.StartBrokered(
                    this,
                    pipeName!,
                    token!,
                    new SnoopAgentOptions
                    {
                        EnableMutation = true,
                        EnableRedaction = false,
                    });

                Console.Error.WriteLine(
                    $"[SnoopWPF.SampleApp] Brokered mode started. Pipe={pipeName}");
            }
            else
            {
                // Co-located / --mcp-stdio mode: MCP server on stdio transport.
                // Console.SetOut(TextWriter.Null) is called inside StartCoLocated (M1-19).
                this.agentHandle = SnoopAgent.StartCoLocated(new SnoopAgentOptions
                {
                    EnableMutation = false,
                    EnableRedaction = true,
                });
            }
        }

        if (smoke)
        {
            // Run smoke self-test on a background thread so the Dispatcher can pump.
            var dispatcher = this.Dispatcher;
            _ = Task.Run(async () =>
            {
                try
                {
                    int exitCode = await RunSmokeTestAsync(dispatcher).ConfigureAwait(false);
                    dispatcher.Invoke(() => Application.Current.Shutdown(exitCode));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[smoke] unhandled exception: {ex.Message}");
                    dispatcher.Invoke(() => Application.Current.Shutdown(2));
                }
            });
        }
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        this.agentHandle?.Dispose();
        base.OnExit(e);
    }

    // -------------------------------------------------------------------------
    // Smoke test — calls wpf_get_session_info via the in-process inspector.
    // Returns exit code: 0 = pass, 1 = assertion failure, 2 = exception.
    // -------------------------------------------------------------------------

    private static async Task<int> RunSmokeTestAsync(System.Windows.Threading.Dispatcher dispatcher)
    {
        // Give the agent a moment to initialise its background server.
        await Task.Delay(300).ConfigureAwait(false);

        try
        {
            // Create an in-process inspector backed by Application.Current.
            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: Application.Current,
                options: new SnoopInspectorOptions
                {
                    TimeoutMs = 10_000,
                    EnableMutation = false,
                    EnableRedaction = false,
                });

            using (inspector)
            {
                // wpf_get_session_info equivalent — verifies the agent can enumerate windows.
                var windows = await inspector.GetWindowsAsync(includeHidden: false, CancellationToken.None)
                    .ConfigureAwait(false);

                if (windows.Count < 1)
                {
                    Console.Error.WriteLine(
                        $"[smoke] FAIL: windows.Count = {windows.Count}, expected >= 1.");
                    return 1;
                }

                Console.Error.WriteLine(
                    $"[smoke] PASS: windows.Count = {windows.Count}.");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[smoke] FAIL: exception: {ex.Message}");
            return 2;
        }
    }

    // -------------------------------------------------------------------------
    // Argument helpers
    // -------------------------------------------------------------------------

    private static bool HasFlag(string[] args, string flag)
        => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the value of a --key=value flag, or null if not present.
    /// Supports both "--key=value" and "--key value" forms.
    /// </summary>
    private static string? GetFlagValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(prefix.Length + 1);
            }
        }

        // "--key value" form
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
