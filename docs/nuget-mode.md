# NuGet Mode — Embedding the Agent in Your WPF App

NuGet mode embeds the SnoopWPF MCP server directly in your WPF application process.
The agent runs in-process, sharing the application's WPF Dispatcher. There is no
injection and no cross-process IPC.

**Requirements:** .NET 8.0+. For older targets use [Injection Mode](injection-mode.md).

---

## Adding the Package

```xml
<!-- MyApp.csproj -->
<PackageReference Include="SnoopWPF.Agent" Version="*" />
```

Or via the CLI:

```bash
dotnet add package SnoopWPF.Agent
```

---

## Minimal Setup

Call `SnoopAgent.Start()` once, from the WPF UI thread, after the application is
initialized. The safest place is in `App.OnStartup`:

```csharp
// App.xaml.cs
using SnoopWPF.Agent.Server;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Default: stdio transport. The MCP server is ready immediately.
        SnoopAgent.Start();
    }
}
```

That is the entire integration. The server stops automatically when the application exits.

---

## SnoopAgentOptions

Pass a `SnoopAgentOptions` instance to `SnoopAgent.Start()` to configure behavior.

```csharp
SnoopAgent.Start(new SnoopAgentOptions
{
    Transport     = TransportMode.Stdio,   // default
    EnableMutation = false,                // default — set true to allow wpf_set_property
    EnableRedaction = true,                // default — set false only for internal tools
    TimeoutMs     = 5000,                 // default per-operation Dispatcher timeout
    PipeName      = null,                 // auto-generated when Transport = Pipe
});
```

### Transport options

| Value | Description |
|-------|-------------|
| `TransportMode.Stdio` | Claude Code launches your app as a subprocess and communicates on stdin/stdout. Zero configuration. |
| `TransportMode.Pipe` | The server listens on a named pipe. Useful when the app is already running and you want to connect a client to it. |

### Stdio transport (recommended)

Configure your MCP client to launch the application directly:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/MyApp.csproj", "--no-build"]
    }
  }
}
```

For a published binary:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "C:/path/to/MyApp.exe"
    }
  }
}
```

### Pipe transport

When `Transport = TransportMode.Pipe`, the server writes the pipe name to stderr:

```
SnoopWPF.Agent MCP server waiting on pipe: snoop-agent-12345
```

The pipe name defaults to `snoop-agent-{pid}`. Override it with `PipeName`:

```csharp
SnoopAgent.Start(new SnoopAgentOptions
{
    Transport = TransportMode.Pipe,
    PipeName  = "my-app-snoop",
});
```

Then configure your client:

```json
{
  "mcpServers": {
    "snoop": {
      "command": "npx",
      "args": ["@modelcontextprotocol/inspector", "--pipe", "\\\\.\\pipe\\my-app-snoop"]
    }
  }
}
```

---

## Lifecycle

`SnoopAgent.Start()` returns a `SnoopAgentHandle`. The agent runs until:

1. The handle is disposed explicitly:

   ```csharp
   private SnoopAgentHandle? _snoopHandle;

   protected override void OnStartup(StartupEventArgs e)
   {
       base.OnStartup(e);
       _snoopHandle = SnoopAgent.Start();
   }

   protected override void OnExit(ExitEventArgs e)
   {
       _snoopHandle?.Dispose();
       base.OnExit(e);
   }
   ```

2. The `Application.Exit` event fires (the agent subscribes automatically).

`SnoopAgent.Start()` throws `InvalidOperationException` if called a second time while
a server is already running. Dispose the existing handle first.

---

## Enabling Mutations

Property mutations are disabled by default. To allow `wpf_set_property`:

```csharp
SnoopAgent.Start(new SnoopAgentOptions
{
    EnableMutation = true,
});
```

When enabled, the agent can set properties whose types are in the hardcoded safe-type
list (see [Security](security.md#typeconverter-safety)). Sensitive properties are
still redacted even when mutation is enabled.

---

## Coexistence with Snoop UI

The agent and the Snoop GUI window can run at the same time without conflict. The
agent uses a separate code path from the GUI and does not interfere with any open
Snoop windows.

To open the Snoop GUI while the agent is running, attach Snoop normally (by
highlighting the window in the Snoop app chooser). Both will inspect the same process.

---

## Conditional Activation

For apps where you only want the agent in development builds:

```csharp
#if DEBUG
SnoopAgent.Start();
#endif
```

Or via an environment variable:

```csharp
if (Environment.GetEnvironmentVariable("ENABLE_SNOOP_AGENT") == "1")
{
    SnoopAgent.Start();
}
```

Or a command-line flag (mimicking the sample app):

```csharp
if (!args.Contains("--no-agent"))
{
    SnoopAgent.Start();
}
```

---

## Troubleshooting

### "SnoopAgent is already running"

You called `SnoopAgent.Start()` more than once. Dispose the first handle before
calling `Start()` again, or gate the call with a null check:

```csharp
private SnoopAgentHandle? _agent;

if (_agent == null)
{
    _agent = SnoopAgent.Start();
}
```

### "SnoopAgent.Start() must be called from a thread that has a WPF Dispatcher"

Call `Start()` from `App.OnStartup` or another UI-thread entry point, not from a
background thread or a static constructor.

### Client connects but tools return NODE_NOT_FOUND immediately

The application may not have created its main window yet. Ensure `Start()` is called
after `InitializeComponent()` or after the main window's `Loaded` event fires.

### MCP client cannot connect (stdio)

Check that the `command` in your MCP config launches the correct executable and that
the working directory is correct. Use `--verbose` with `snoop-mcp` for injection mode,
or check the app's stderr output for NuGet mode.

### Performance: Dispatcher timeout errors

The default per-operation timeout is 5 seconds. If your app performs heavy work on
the UI thread, increase `TimeoutMs`:

```csharp
SnoopAgent.Start(new SnoopAgentOptions { TimeoutMs = 15000 });
```
