namespace SnoopWPF.SampleApp;

using System;
using System.Windows;
using SnoopWPF.Agent.Server;

/// <summary>
/// Entry point for the SnoopWPF Sample Application.
/// Demonstrates NuGet-mode MCP agent integration via SnoopAgent.Start().
/// </summary>
public partial class App : Application
{
    private SnoopAgentHandle? agentHandle;
    private HiddenWindow? hiddenWindow;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse --no-agent flag to allow injection-mode testing without the NuGet agent running.
        bool noAgent = Array.Exists(e.Args, a => a.Equals("--no-agent", StringComparison.OrdinalIgnoreCase));

        if (!noAgent)
        {
            // Start the MCP agent on stdio transport (default).
            // The endpoint info is written to %TEMP%\snoop-agent-{pid}.json.
            // Do NOT log the bearer token — integration tests read it from the discovery file.
            this.agentHandle = SnoopAgent.Start(new SnoopAgentOptions
            {
                EnableMutation = false,
                EnableRedaction = true,
            });

            Console.WriteLine("SnoopWPF.Agent MCP server started (stdio transport).");
        }

        // Keep a hidden window open for the lifetime of the application.
        // This tests wpf_get_windows with includeHidden=true/false and screenshot edge cases.
        this.hiddenWindow = new HiddenWindow();
        this.hiddenWindow.Show(); // Show then hide so the window is initialized and measurable
        this.hiddenWindow.Hide();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        this.agentHandle?.Dispose();
        base.OnExit(e);
    }
}
