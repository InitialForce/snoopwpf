// snoop-mcp: injection-mode MCP host.
// Injects SnoopWPF.Agent.Injection.dll into a target WPF process and runs an
// MCP server on stdio that proxies requests through the named pipe to the agent.

namespace SnoopWPF.Agent.Host;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snoop;
using Snoop.Data;
using SnoopWPF.Agent.Remote;

internal static class Program
{
    private static bool verbose;

    private static int Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<CommandLineOptions>(args);
        return result.MapResult(
            opts => RunAsync(opts).GetAwaiter().GetResult(),
            _ => 1);
    }

    private static async Task<int> RunAsync(CommandLineOptions opts)
    {
        verbose = opts.Verbose;

        // ------------------------------------------------------------------
        // 1. Resolve target process.
        // ------------------------------------------------------------------
        Process targetProcess;
        try
        {
            targetProcess = ResolveTargetProcess(opts);
        }
        catch (Exception ex)
        {
            WriteError($"Could not locate target process: {ex.Message}");
            return 1;
        }

        int targetPid = targetProcess.Id;
        WriteVerbose($"Target process: {targetProcess.ProcessName} (PID={targetPid})");

        // ------------------------------------------------------------------
        // 2. Target process ownership check.
        //    Verify the target process is owned by the current user.
        //    Warn and require --force (not yet implemented) on mismatch.
        // ------------------------------------------------------------------
        try
        {
            VerifyProcessOwnership(targetProcess);
        }
        catch (OwnershipMismatchException ex)
        {
            WriteError(ex.Message);
            WriteError("Use --force to override the ownership check (not yet implemented).");
            return 1;
        }
        catch (Exception ex)
        {
            // Ownership check failed for a different reason — warn but proceed.
            WriteVerbose($"Warning: could not verify target process ownership: {ex.Message}");
        }

        // ------------------------------------------------------------------
        // 3. Generate session token (256-bit random) and pipe name (GUID).
        //    NEVER log either of these values.
        // ------------------------------------------------------------------
        byte[] sessionTokenBytes = RandomNumberGenerator.GetBytes(32);
        string sessionToken = Convert.ToHexString(sessionTokenBytes);
        string pipeName = Guid.NewGuid().ToString("N");

        WriteVerbose("Generated session token and pipe name.");

        // ------------------------------------------------------------------
        // 4. Write transient settings file (pipe name + token) for the injected DLL.
        //    File is created via GetTempFileName (restricted to current user on Windows).
        // ------------------------------------------------------------------
        var settings = new TransientSettingsData
        {
            PipeName = pipeName,
            SessionToken = sessionToken,
            StartTarget = SnoopStartTarget.HeadlessAgent,
        };

        string settingsFile = settings.WriteToFile();
        WriteVerbose("Settings file written.");

        // ------------------------------------------------------------------
        // 5. Open PipeConnection BEFORE injection so the pipe server is ready
        //    when the agent starts connecting.
        //    PipeOptions.CurrentUserOnly (applied in PipeConnection) restricts access to current user.
        // ------------------------------------------------------------------
        var pipeConnection = new PipeConnection(pipeName, targetPid);
        WriteVerbose("Pipe server created, waiting for agent connection.");

        try
        {
            // ------------------------------------------------------------------
            // 6. Inject the agent DLL.
            //    ProcessInfo.InjectAgent calls InjectorLauncherManager.Launch with
            //    the agent assembly/class/method names and the settings file path.
            // ------------------------------------------------------------------
            var processInfo = new ProcessInfo(targetProcess);
            try
            {
                processInfo.InjectAgent(IntPtr.Zero, settingsFile);
                WriteVerbose("Injection complete.");
            }
            catch (Exception ex)
            {
                WriteError($"Injection failed: {ex.Message}");
                return 1;
            }

            // ------------------------------------------------------------------
            // 7. Wait for the injected agent to connect to the pipe.
            // ------------------------------------------------------------------
            using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(opts.Timeout));
            try
            {
                await pipeConnection.WaitForConnectionAsync(handshakeCts.Token).ConfigureAwait(false);
                WriteVerbose("Agent connected to pipe.");
            }
            catch (OperationCanceledException)
            {
                WriteError($"Timed out waiting for agent to connect (timeout={opts.Timeout}s).");
                return 1;
            }

            // ------------------------------------------------------------------
            // 8. Perform handshake: send challenge, verify response + session token.
            // ------------------------------------------------------------------
            try
            {
                await pipeConnection.HandshakeAsync(sessionToken, handshakeCts.Token).ConfigureAwait(false);
                WriteVerbose("Handshake completed successfully.");
            }
            catch (Exception ex)
            {
                WriteError($"Handshake failed: {ex.Message}");
                return 1;
            }
            finally
            {
                // Zero the token bytes from memory after handshake.
                Array.Clear(sessionTokenBytes, 0, sessionTokenBytes.Length);
            }

            // ------------------------------------------------------------------
            // 9. Create the proxy (host-side ISnoopInspector) and start the pump.
            // ------------------------------------------------------------------
            await using var proxy = new PipeSnoopInspectorProxy(pipeConnection);
            proxy.StartPump();

            WriteVerbose("Proxy ready. Starting MCP server on stdio.");

            // ------------------------------------------------------------------
            // 10. Build and run MCP server on stdio.
            // ------------------------------------------------------------------
            using var cts = new CancellationTokenSource();

            // Gracefully handle Ctrl+C and SIGTERM.
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                cts.Cancel();
            };

            await RunMcpServerAsync(proxy, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            pipeConnection.Dispose();

            // Best-effort cleanup of settings file (injected DLL deletes it on load,
            // but clean up here in case injection never started).
            TryDeleteSettingsFile(settingsFile);
        }

        return 0;
    }

    // ------------------------------------------------------------------
    // MCP server bootstrap
    // ------------------------------------------------------------------

    private static async Task RunMcpServerAsync(
        SnoopWPF.Agent.Contracts.ISnoopInspector inspector,
        CancellationToken ct)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "snoop-wpf",
                Version = version,
            },
        };

        var services = new ServiceCollection();
        services.AddSingleton<SnoopWPF.Agent.Contracts.ISnoopInspector>(inspector);

        var toolsAssembly = typeof(SnoopWPF.Agent.Tools.SessionInfoTool).Assembly;
        services
            .AddMcpServer()
            .WithToolsFromAssembly(toolsAssembly);

        var sp = services.BuildServiceProvider();

        var transport = new StdioServerTransport(serverOptions);
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Target process resolution
    // ------------------------------------------------------------------

    private static Process ResolveTargetProcess(CommandLineOptions opts)
    {
        if (opts.Pid.HasValue && !string.IsNullOrEmpty(opts.WindowTitle))
        {
            throw new ArgumentException("Specify either --pid or --window-title, not both.");
        }

        if (opts.Pid.HasValue)
        {
            return Process.GetProcessById(opts.Pid.Value);
        }

        if (!string.IsNullOrEmpty(opts.WindowTitle))
        {
            return FindProcessByWindowTitle(opts.WindowTitle);
        }

        throw new ArgumentException("Either --pid or --window-title is required.");
    }

    private static Process FindProcessByWindowTitle(string pattern)
    {
        // Case-insensitive substring match against all process main window titles.
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            catch
            {
                // Some processes may throw when accessing MainWindowTitle.
            }
        }

        throw new InvalidOperationException($"No process found with window title containing \"{pattern}\".");
    }

    // ------------------------------------------------------------------
    // Process ownership verification
    // ------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static void VerifyProcessOwnership(Process process)
    {
        // Get current user SID.
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var currentUserSid = currentIdentity.User
            ?? throw new InvalidOperationException("Could not determine current user SID.");

        // Get the owner SID of the target process.
        var targetUserSid = GetProcessOwnerSid(process);

        if (targetUserSid is null)
        {
            // Could not determine — warn via verbose but do not block.
            WriteVerbose("Warning: could not read target process owner SID; skipping ownership check.");
            return;
        }

        if (!targetUserSid.Equals(currentUserSid))
        {
            throw new OwnershipMismatchException(
                $"Target process (PID={process.Id}) is owned by a different user " +
                $"(target={targetUserSid}, current={currentUserSid}). " +
                "Injecting into another user's process is not allowed.");
        }

        WriteVerbose("Process ownership verified.");
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier? GetProcessOwnerSid(Process process)
    {
        // Open process with QueryInformation access to read the token.
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.QueryInformation,
            false,
            (uint)process.Id);

        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Open the process token.
            if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenAccessRights.Query, out var tokenHandle))
            {
                return null;
            }

            try
            {
                return GetTokenUserSid(tokenHandle);
            }
            finally
            {
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
        finally
        {
            // Close the process handle immediately — minimal handle lifetime.
            NativeMethods.CloseHandle(processHandle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier? GetTokenUserSid(IntPtr tokenHandle)
    {
        // First call to determine required buffer size.
        NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenInformationClass.TokenUser, IntPtr.Zero, 0, out int requiredLength);

        if (requiredLength <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            if (!NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenInformationClass.TokenUser, buffer, requiredLength, out _))
            {
                return null;
            }

            // TOKEN_USER: { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } }
            // The first field is a pointer to the SID.
            var sidPtr = Marshal.ReadIntPtr(buffer);
            if (sidPtr == IntPtr.Zero)
            {
                return null;
            }

            return new SecurityIdentifier(sidPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void WriteError(string message)
    {
        Console.Error.WriteLine($"[snoop-mcp] ERROR: {message}");
    }

    private static void WriteVerbose(string message)
    {
        if (verbose)
        {
            Console.Error.WriteLine($"[snoop-mcp] {message}");
        }
    }

    private static void TryDeleteSettingsFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    // ------------------------------------------------------------------
    // Custom exception
    // ------------------------------------------------------------------

#pragma warning disable CA1032 // OwnershipMismatchException is internal; only the message ctor is needed.
    private sealed class OwnershipMismatchException : Exception
    {
        internal OwnershipMismatchException(string message)
            : base(message)
        {
        }
    }
#pragma warning restore CA1032

    // ------------------------------------------------------------------
    // Native methods (CA1060: P/Invokes in a NativeMethods class)
    // ------------------------------------------------------------------

    private static class NativeMethods
    {
        [Flags]
        internal enum ProcessAccessFlags : uint
        {
            QueryInformation = 0x0400,
        }

        internal enum TokenInformationClass
        {
            TokenUser = 1,
        }

        internal enum TokenAccessRights : uint
        {
            Query = 0x0008,
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        internal static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, TokenAccessRights desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            TokenInformationClass tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);
    }
}
