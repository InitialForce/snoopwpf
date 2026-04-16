namespace SnoopWPF.Agent.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Snoop;
using Snoop.Data;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Remote;

// ---------------------------------------------------------------------------
// Verb option types
// ---------------------------------------------------------------------------

[Verb("list", HelpText = "List top-level WPF windows in the target process.")]
internal sealed class ListOptions : GlobalOptions
{
}

[Verb("tree", HelpText = "Show the visual tree of the target process.")]
internal sealed class TreeOptions : GlobalOptions
{
    [Option("node", Required = false, HelpText = "Root node ID to start the tree from.")]
    public string? NodeId { get; set; }

    [Option("depth", Required = false, Default = 8, HelpText = "Maximum tree depth.")]
    public int Depth { get; set; }
}

[Verb("props", HelpText = "Show properties of a node.")]
internal sealed class PropsOptions : GlobalOptions
{
    [Value(0, Required = true, MetaName = "node-id", HelpText = "Node ID to inspect.")]
    public string NodeId { get; set; } = string.Empty;

    [Option("filter", Required = false, HelpText = "Filter by property name (substring).")]
    public string? Filter { get; set; }
}

[Verb("inspect", HelpText = "Show a rich summary of a single element.")]
internal sealed class InspectOptions : GlobalOptions
{
    [Value(0, Required = true, MetaName = "node-id", HelpText = "Node ID to inspect.")]
    public string NodeId { get; set; } = string.Empty;
}

[Verb("find", HelpText = "Find elements by type name.")]
internal sealed class FindOptions : GlobalOptions
{
    [Option("type", Required = false, HelpText = "Type name to search for.")]
    public string? TypeName { get; set; }

    [Option("name", Required = false, HelpText = "Element name to search for.")]
    public string? Name { get; set; }

    [Option("max", Required = false, Default = 50, HelpText = "Maximum results.")]
    public int Max { get; set; }
}

[Verb("diag", HelpText = "Run diagnostics on the target process.")]
internal sealed class DiagOptions : GlobalOptions
{
    [Option("node", Required = false, HelpText = "Node ID scope (optional).")]
    public string? NodeId { get; set; }
}

[Verb("screenshot", HelpText = "Capture a screenshot of a node or the full window.")]
internal sealed class ScreenshotOptions : GlobalOptions
{
    [Value(0, Required = false, MetaName = "node-id", HelpText = "Node ID to screenshot (optional).")]
    public string? NodeId { get; set; }

    [Option('o', "out", Required = false, HelpText = "Output file path for the PNG.")]
    public string? OutputPath { get; set; }
}

// ---------------------------------------------------------------------------
// Global options shared by all verbs
// ---------------------------------------------------------------------------

internal abstract class GlobalOptions
{
    [Option('p', "pid", Required = true, HelpText = "PID of the target WPF process.")]
    public int Pid { get; set; }

    [Option('j', "json", Required = false, Default = false, HelpText = "Output raw JSON instead of formatted text.")]
    public bool Json { get; set; }

    [Option("force", Required = false, Default = false, HelpText = "Skip process ownership check (use with caution).")]
    public bool Force { get; set; }
}

// ---------------------------------------------------------------------------
// Program entry point
// ---------------------------------------------------------------------------

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var result = await Parser.Default.ParseArguments<
                ListOptions,
                TreeOptions,
                PropsOptions,
                InspectOptions,
                FindOptions,
                DiagOptions,
                ScreenshotOptions>(args)
                .MapResult(
                    (ListOptions opts) => RunAsync(opts, ExecuteListAsync),
                    (TreeOptions opts) => RunAsync(opts, ExecuteTreeAsync),
                    (PropsOptions opts) => RunAsync(opts, ExecutePropsAsync),
                    (InspectOptions opts) => RunAsync(opts, ExecuteInspectAsync),
                    (FindOptions opts) => RunAsync(opts, ExecuteFindAsync),
                    (DiagOptions opts) => RunAsync(opts, ExecuteDiagAsync),
                    (ScreenshotOptions opts) => RunAsync(opts, ExecuteScreenshotAsync),
                    errs => Task.FromResult(1));

            return result;
        }
        catch (SnoopException ex)
        {
            Console.Error.WriteLine($"Error [{ex.Code}]: {ex.Message}");

            if (ex.Suggestions is { Length: > 0 })
            {
                Console.Error.WriteLine($"Suggestion: {string.Join("; ", ex.Suggestions)}");
            }

            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 3;
        }
    }

    // -------------------------------------------------------------------------
    // Generic run wrapper — inject, connect, call, disconnect
    // -------------------------------------------------------------------------

    private static async Task<int> RunAsync<TOptions>(
        TOptions opts,
        Func<ISnoopInspector, TOptions, CancellationToken, Task<int>> action)
        where TOptions : GlobalOptions
    {
        // Ownership check.
        if (!opts.Force)
        {
            CheckProcessOwnership(opts.Pid);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var (proxy, cleanup) = await InjectAndConnectAsync(opts.Pid, cts.Token);

        try
        {
            return await action(proxy, opts, cts.Token);
        }
        finally
        {
            await cleanup(proxy);
        }
    }

    // -------------------------------------------------------------------------
    // Injection + pipe handshake
    // -------------------------------------------------------------------------

    private static async Task<(PipeSnoopInspectorProxy Proxy, Func<PipeSnoopInspectorProxy, Task> Cleanup)>
        InjectAndConnectAsync(int pid, CancellationToken ct)
    {
        // 1. Generate a random session token via CSPRNG.
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var sessionToken = Convert.ToHexString(tokenBytes);
        Array.Clear(tokenBytes, 0, tokenBytes.Length);

        // 2. Generate a random pipe name.
        var pipeName = Guid.NewGuid().ToString("N");

        // 3. Create the pipe server (with CurrentUserOnly ACL) BEFORE injecting
        //    so it is ready when the agent tries to connect.
        var connection = new PipeConnection(pipeName, expectedClientPid: pid);

        // 4. Build settings and inject.
        var settings = new TransientSettingsData
        {
            PipeName = pipeName,
            SessionToken = sessionToken,
        };

        var processInfo = new ProcessInfo(pid);

        InjectorLauncherManager.Launch(
            processInfo,
            targetHwnd: IntPtr.Zero,
            assembly: "SnoopWPF.Agent.Injection",
            className: "SnoopWPF.Agent.Injection.SnoopAgentEntryPoint",
            methodName: "Start",
            transientSettingsFile: settings.WriteToFile());

        // 5. Wait for agent to connect (15s timeout).
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

        try
        {
            await connection.WaitForConnectionAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            connection.Dispose();
            throw new SnoopException(
                SnoopErrorCode.SessionNotFound,
                $"Timed out waiting for injected agent in PID {pid} to connect.",
                suggestions: new[] { "Verify the target process is a WPF application." });
        }

        // 6. Handshake.
        await connection.HandshakeAsync(sessionToken, ct);

        // 7. Zero the session token string reference.
        sessionToken = string.Empty;

        // 8. Wrap in proxy and start pump.
        var proxy = new PipeSnoopInspectorProxy(connection);
        proxy.StartPump();

        static async Task Cleanup(PipeSnoopInspectorProxy p)
        {
            await p.DisposeAsync();
        }

        return (proxy, Cleanup);
    }

    // -------------------------------------------------------------------------
    // Process ownership check
    // -------------------------------------------------------------------------

    private static void CheckProcessOwnership(int pid)
    {
        try
        {
            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid is null)
            {
                return;
            }

            var process = Process.GetProcessById(pid);
            var processOwnerSid = NativeMethods.GetProcessOwnerSid(process);

            if (processOwnerSid is not null && !processOwnerSid.Equals(currentSid))
            {
                Console.Error.WriteLine(
                    $"Warning: target process PID {pid} is owned by a different user ({processOwnerSid}). " +
                    "Use --force to bypass this check.");
                Environment.Exit(4);
            }
        }
        catch (InvalidOperationException)
        {
            // Process not found — will fail later during injection with a cleaner error.
        }
        catch (Exception)
        {
            // Best-effort — can't determine ownership; allow injection to proceed.
        }
    }

    // -------------------------------------------------------------------------
    // Command: list
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteListAsync(
        ISnoopInspector inspector,
        ListOptions opts,
        CancellationToken ct)
    {
        var windows = await inspector.GetWindowsAsync(includeHidden: false, ct);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(windows);
        }
        else
        {
            OutputFormatter.WriteWindows(windows);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: tree
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteTreeAsync(
        ISnoopInspector inspector,
        TreeOptions opts,
        CancellationToken ct)
    {
        var result = await inspector.GetVisualTreeAsync(
            rootNodeId: opts.NodeId,
            maxDepth: opts.Depth,
            treeType: "Visual",
            includeProperties: null,
            ct);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(result);
        }
        else
        {
            Console.WriteLine($"Visual tree (depth <= {opts.Depth}, returned {result.ReturnedNodeCount} nodes{(result.Truncated ? ", truncated" : string.Empty)}):");
            Console.WriteLine();
            OutputFormatter.WriteTree(result.Root);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: props
    // -------------------------------------------------------------------------

    private static async Task<int> ExecutePropsAsync(
        ISnoopInspector inspector,
        PropsOptions opts,
        CancellationToken ct)
    {
        var allProps = new List<PropertyDto>();
        string? cursor = null;

        do
        {
            var page = await inspector.GetPropertiesAsync(
                nodeId: opts.NodeId,
                filter: opts.Filter,
                category: null,
                includeDefaults: true,
                cursor: cursor,
                take: 200,
                ct);

            allProps.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(allProps);
        }
        else
        {
            Console.WriteLine($"Properties of {opts.NodeId} ({allProps.Count} total):");
            Console.WriteLine("  Flags: L=LocallySet B=Bound !=BindingError R=ReadOnly *=Redacted");
            Console.WriteLine();
            OutputFormatter.WriteProperties(allProps);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: inspect
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteInspectAsync(
        ISnoopInspector inspector,
        InspectOptions opts,
        CancellationToken ct)
    {
        var element = await inspector.InspectElementAsync(opts.NodeId, ct);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(element);
        }
        else
        {
            OutputFormatter.WriteInspectElement(element);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: find
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteFindAsync(
        ISnoopInspector inspector,
        FindOptions opts,
        CancellationToken ct)
    {
        var result = await inspector.FindElementsAsync(
            typeName: opts.TypeName,
            name: opts.Name,
            rootNodeId: null,
            conditions: null,
            treeType: "Visual",
            maxResults: opts.Max,
            ct);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(result);
        }
        else
        {
            OutputFormatter.WriteFindResults(result);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: diag
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteDiagAsync(
        ISnoopInspector inspector,
        DiagOptions opts,
        CancellationToken ct)
    {
        var allItems = new List<DiagnosticItemDto>();
        string? cursor = null;

        do
        {
            var page = await inspector.RunDiagnosticsAsync(
                nodeId: opts.NodeId,
                providers: null,
                minLevel: null,
                cursor: cursor,
                take: 100,
                ct);

            allItems.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        if (opts.Json)
        {
            OutputFormatter.WriteJson(allItems);
        }
        else
        {
            Console.WriteLine($"Diagnostics ({allItems.Count} items):");
            Console.WriteLine();
            OutputFormatter.WriteDiagnostics(allItems);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Command: screenshot
    // -------------------------------------------------------------------------

    private static async Task<int> ExecuteScreenshotAsync(
        ISnoopInspector inspector,
        ScreenshotOptions opts,
        CancellationToken ct)
    {
        var result = await inspector.CaptureScreenshotAsync(opts.NodeId, ct);

        if (opts.Json)
        {
            // Emit metadata only as JSON; PNG bytes are binary and should go to --out.
            OutputFormatter.WriteJson(result.Metadata);
        }
        else
        {
            OutputFormatter.WriteScreenshotResult(result, opts.OutputPath);
        }

        if (!string.IsNullOrEmpty(opts.OutputPath) && result.PngBytes.Length > 0)
        {
            await File.WriteAllBytesAsync(opts.OutputPath, result.PngBytes, ct);
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // P/Invoke — Native methods class as required by CA1060
    // -------------------------------------------------------------------------

    private static class NativeMethods
    {
        private const uint TokenQuery = 0x0008;

        internal static SecurityIdentifier? GetProcessOwnerSid(Process process)
        {
            try
            {
                if (!OpenProcessToken(process.Handle, TokenQuery, out var tokenHandle))
                {
                    return null;
                }

                using (tokenHandle)
                {
                    using var identity = new WindowsIdentity(tokenHandle.DangerousGetHandle());
                    return identity.User;
                }
            }
            catch
            {
                return null;
            }
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle);
    }
}
