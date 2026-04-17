// SnoopWPF.Minimal — minimal MCP-over-stdio sample (~30 LOC).
//
// Build & run (serves MCP on stdio):
//   dotnet run --project Samples/SnoopWPF.Minimal
//
// Then point any MCP client at this process:
//   { "command": "dotnet", "args": ["run", "--project", "Samples/SnoopWPF.Minimal"] }

namespace SnoopWPF.Minimal;

using System.Windows;
using SnoopWPF.Agent.Server;

/// <summary>
/// Minimal WPF application that starts the SnoopWPF MCP agent on stdio transport.
/// The agent is started in <see cref="OnStartup"/> and shut down in <see cref="OnExit"/>.
/// </summary>
public partial class App : Application
{
    private SnoopAgentHandle? agentHandle;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Start the MCP server on stdio transport.
        // Console.Out is redirected to null internally so WPF debug output does not
        // corrupt the MCP framing.
        this.agentHandle = SnoopAgent.StartCoLocated();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        this.agentHandle?.Dispose();
        base.OnExit(e);
    }
}
