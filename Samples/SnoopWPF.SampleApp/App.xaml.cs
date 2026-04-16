namespace SnoopWPF.SampleApp;

using System;
using System.Windows;
using SnoopWPF.Agent;

/// <summary>
/// Entry point for the SnoopWPF Sample Application.
/// Demonstrates NuGet-mode MCP agent integration via SnoopAgent.Start().
/// </summary>
public partial class App : Application
{
    private SnoopAgentHandle? agentHandle;
    private HiddenWindow? hiddenWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse --no-agent flag to allow injection-mode testing without the NuGet agent running.
        bool noAgent = Array.Exists(e.Args, a => a.Equals("--no-agent", StringComparison.OrdinalIgnoreCase));

        if (!noAgent)
        {
            // Start the MCP agent. The server binds on 127.0.0.1 (loopback only).
            // The bearer token and endpoint URI are written to %TEMP%\snoop-agent-{pid}.json.
            // Do NOT log the bearer token — integration tests read it from the discovery file.
            agentHandle = SnoopAgent.Start(this, new SnoopAgentOptions
            {
                EnableMutation = false,
                EnableRedaction = true,
            });

            Console.WriteLine($"SnoopWPF.Agent MCP server listening at {agentHandle.EndpointUri}");
        }

        // Keep a hidden window open for the lifetime of the application.
        // This tests wpf_get_windows with includeHidden=true/false and screenshot edge cases.
        hiddenWindow = new HiddenWindow();
        hiddenWindow.Show(); // Show then hide so the window is initialized and measurable
        hiddenWindow.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        agentHandle?.Stop();
        base.OnExit(e);
    }
}
