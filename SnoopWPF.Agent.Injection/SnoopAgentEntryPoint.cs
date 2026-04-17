namespace SnoopWPF.Agent.Injection;

// IMPORTANT: This class is the injection entry point. The CLR calls Start() via
// ExecuteInDefaultAppDomain (net462) or a bootstrapper shim (net6+).
// The return type MUST be int — it maps directly to HRESULT on net462.
//
// CRITICAL: The very first thing Start() must do — BEFORE referencing any types from
// SnoopWPF.Agent.Engine, SnoopWPF.Agent.Contracts, or Snoop.Core — is install the
// AssemblyResolve handler. This is required on net462 where shadow-copy AppDomains
// cannot find sibling assemblies by default.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Threading;

/// <summary>
/// Injection entry point. The GenericInjector calls <see cref="Start"/> via
/// <c>ICorRuntimeHost::ExecuteInDefaultAppDomain</c> (net462) or equivalent shim (net6+).
/// </summary>
public static class SnoopAgentEntryPoint
{
    private static readonly object StartLock = new object();
    private static volatile bool started;

    // Stored so we can unregister on shutdown.
    private static ResolveEventHandler? assemblyResolveHandler;

    /// <summary>
    /// Entry point called by the injector.
    /// </summary>
    /// <param name="settingsFile">Path to the XML settings file containing PipeName and SessionToken.</param>
    /// <returns>0 on success, 1 on failure.</returns>
    public static int Start(string settingsFile)
    {
        // ---------------------------------------------------------------
        // Step 1: Install assembly resolver IMMEDIATELY — before any other
        // code that references Engine/Contracts/Snoop.Core types.
        // ---------------------------------------------------------------
        InstallAssemblyResolver();

        // ---------------------------------------------------------------
        // Step 2: Guard against re-entry (injected twice into same process).
        // ---------------------------------------------------------------
        lock (StartLock)
        {
            if (started)
            {
                return 0; // Already running — success.
            }

            started = true;
        }

        try
        {
            StartCore(settingsFile);
            return 0;
        }
        catch (Exception ex)
        {
            // Log to debug output only — never to file (could contain sensitive path info).
            System.Diagnostics.Debug.WriteLine($"[SnoopAgent] Start failed: {ex.GetType().Name}");
            return 1;
        }
    }

    // -----------------------------------------------------------------
    // Assembly resolver — MUST be installed before touching Engine types
    // -----------------------------------------------------------------

    private static void InstallAssemblyResolver()
    {
        // Store reference so we can unregister on shutdown.
        assemblyResolveHandler = OnAssemblyResolve;
        AppDomain.CurrentDomain.AssemblyResolve += assemblyResolveHandler;
    }

    private static void UninstallAssemblyResolver()
    {
        if (assemblyResolveHandler != null)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= assemblyResolveHandler;
            assemblyResolveHandler = null;
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        // Only intercept Snoop-related assemblies.
        // Let the CLR's default resolution handle everything else to avoid shadowing
        // target-app assemblies or framework assemblies.
        var simpleName = new AssemblyName(args.Name).Name ?? string.Empty;
        if (!simpleName.StartsWith("SnoopWPF.", StringComparison.Ordinal)
            && !simpleName.StartsWith("Snoop.", StringComparison.Ordinal)
            && simpleName != "SnoopWPF"
            && simpleName != "Snoop")
        {
            return null;
        }

        // Resolve from the same directory as this DLL.
        try
        {
            var thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (thisDir == null)
            {
                return null;
            }

            var candidatePath = Path.Combine(thisDir, simpleName + ".dll");
            if (File.Exists(candidatePath))
            {
                return Assembly.LoadFrom(candidatePath);
            }

            // Also try subdirectory named after the TFM (e.g. net462/).
            var tfmDir = Path.Combine(thisDir, "net462");
            var tfmPath = Path.Combine(tfmDir, simpleName + ".dll");
            if (File.Exists(tfmPath))
            {
                return Assembly.LoadFrom(tfmPath);
            }
        }
        catch (Exception)
        {
            // Never throw from AssemblyResolve — return null to let CLR continue.
        }

        return null;
    }

    // -----------------------------------------------------------------
    // Core startup — references Engine/Contracts types (safe after resolver)
    // -----------------------------------------------------------------

    private static void StartCore(string settingsFile)
    {
        // ---------------------------------------------------------------
        // Step 3: Read settings, extract token as bytes, then delete file.
        // ---------------------------------------------------------------
        byte[] sessionTokenBytes;
        string pipeName;

        LoadAndConsumeSettings(settingsFile, out pipeName, out sessionTokenBytes);

        // ---------------------------------------------------------------
        // Step 4: Obtain the WPF Dispatcher for the current application.
        // The injected code runs on a CLR thread; the target's Dispatcher
        // belongs to the UI thread.
        // ---------------------------------------------------------------
        var dispatcher = GetTargetDispatcher();

        // ---------------------------------------------------------------
        // Step 5: Create the inspector and pipe server, then run.
        // The pipe server runs on a background thread so it doesn't block
        // the injected thread (which the CLR shim may need to return).
        // ---------------------------------------------------------------
        //
        // FX2-C4 (MAJOR-I4 / PRD-M5): construct the injection SessionPolicy and pass
        // both policy and inspector options. Without these, EnsureMutationTier sees a
        // null sessionPolicy and L1 mutation paths become REACHABLE in injection mode,
        // violating MF-11.
        //
        // SessionMode.Injection forces:
        //   - EnableRedaction = true (MF-11)
        //   - MaxTier = L0ReadOnly (S7)
        //   - AllowSensitiveRetention = false
        var injectionAgentOptions = new SnoopWPF.Agent.Contracts.SnoopAgentOptions
        {
            EnableMutation = false,
            EnableAutomation = false,
            EnableRedaction = true,
        };

#if !NET6_0_OR_GREATER
        // FX6-Z1: Option B — refuse EnableMutation=true on net462 targets at session start.
        // AuditLogWriter depends on System.Threading.Channels which is not available on net462.
        // A mutation session with no audit trail violates the security contract.
        // See ARCHITECTURE-CHANGE-2026-04-17-Z1-NET462-MUTATION-REFUSED.md.
        if (injectionAgentOptions.EnableMutation)
        {
            throw new SnoopWPF.Agent.Contracts.SnoopException(
                SnoopWPF.Agent.Contracts.SnoopErrorCode.UnsupportedOnNet462,
                "EnableMutation=true is not supported when the agent is injected into a .NET Framework 4.6.2 target. " +
                "The audit log subsystem requires System.Threading.Channels (net6+). " +
                "A mutation session with no audit trail is not permitted. " +
                "Use a .NET 6 or later target application to enable mutation.");
        }
#endif
        var injectionPolicy = SnoopWPF.Agent.Contracts.SessionPolicy.Create(
            SnoopWPF.Agent.Contracts.SessionMode.Injection,
            injectionAgentOptions);

        var inspectorOptions = new SnoopWPF.Agent.Engine.SnoopInspectorOptions
        {
            EnableMutation = injectionPolicy.EnableMutation,
            EnableRedaction = injectionPolicy.EnableRedaction,
            EnableAutomation = injectionPolicy.EnableAutomation,
            AllowSensitiveRetention = injectionPolicy.AllowSensitiveRetention,
        };

        var inspector = new SnoopWPF.Agent.Engine.SnoopInspector(
            dispatcher,
            rootTarget: null, // null → engine uses Application.Current
            options: inspectorOptions,
            sessionPolicy: injectionPolicy);

        var server = new PipeAgentServer(pipeName, sessionTokenBytes, inspector);

        // Zero the token bytes from memory after handing off to PipeAgentServer
        // (PipeAgentServer has its own copy; we clear ours).
        Array.Clear(sessionTokenBytes, 0, sessionTokenBytes.Length);
        sessionTokenBytes = null!;

        var cts = new CancellationTokenSource();

        // Run the server loop on a dedicated background thread.
        var serverThread = new Thread(() =>
        {
            try
            {
                server.RunAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SnoopAgent] Server loop error: {ex.GetType().Name}");
            }
            finally
            {
                server.Dispose();
                inspector.Dispose();
            }
        });

        serverThread.Name = "SnoopAgentPipeServer";
        serverThread.IsBackground = true;
        serverThread.Start();

        // Hook AppDomain.ProcessExit to trigger graceful shutdown.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // FX5-injection-disposal-order: Cancel first so serverThread's ReadAsync
            // throws OCE and exits the loop, then JOIN before disposing so that
            // in-flight request handlers can no longer reach writeLock after it is
            // freed. Without the join, server.Dispose() races with task continuations
            // still holding writeLock.WaitAsync — causing silent ObjectDisposedException.
            cts.Cancel();

            if (!serverThread.Join(TimeSpan.FromSeconds(5)))
            {
                // Degraded path: serverThread did not observe cancellation in time.
                // Log via Trace and fall through to force-dispose.
                System.Diagnostics.Trace.WriteLine(
                    "[SnoopAgentEntryPoint] serverThread did not exit within 5s of cancellation — forcing disposal.");
            }

            // FX4-MEM-C1: dispose the CTS so the kernel WaitHandle is released promptly.
            // Must come after Cancel() so the token is observed before the handle closes.
            cts.Dispose();

            // server and inspector may already be disposed by the serverThread finally
            // block; both types are safe to call Dispose() on more than once.
            server.Dispose();
            inspector.Dispose();

            // Unregister the AssemblyResolve handler so it doesn't outlive the agent.
            UninstallAssemblyResolver();
        };
    }

    // -----------------------------------------------------------------
    // Settings loading with secure token handling
    // -----------------------------------------------------------------

    private static void LoadAndConsumeSettings(string settingsFile, out string pipeName, out byte[] sessionTokenBytes)
    {
        // Load settings (XmlSerializer-based).
        var settings = Snoop.Data.TransientSettingsData.LoadCurrent(settingsFile);

        if (string.IsNullOrEmpty(settings.PipeName))
        {
            throw new InvalidOperationException("PipeName is missing from agent settings.");
        }

        pipeName = settings.PipeName!;

        // Extract token as bytes BEFORE deleting the file.
        var tokenString = settings.SessionToken ?? string.Empty;
        sessionTokenBytes = Encoding.UTF8.GetBytes(tokenString);

        // Zero the string reference in settings (strings are immutable, but clearing
        // the field removes the reference so GC can collect it sooner).
        settings.SessionToken = null;

        // Delete the settings file — it contained the session token.
        try
        {
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
        }
        catch (Exception)
        {
            // Best-effort deletion. If it fails, the token is still short-lived.
        }
    }

    // -----------------------------------------------------------------
    // Dispatcher acquisition
    // -----------------------------------------------------------------

    private static Dispatcher GetTargetDispatcher()
    {
        // Primary: Application.Current.Dispatcher — most robust, works on all .NET versions.
        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null)
            {
                return app.Dispatcher;
            }
        }
        catch (Exception)
        {
            // Application.Current may throw in some AppDomain configurations.
        }

#if !NET6_0_OR_GREATER
        // net462 fallback: enumerate live dispatchers via private reflection.
        // This field was renamed/removed on .NET 6+ WPF, so guard with TFM condition.
        // On net6+ we rely on Application.Current (above) or throw below.
        var dispatcherType = typeof(Dispatcher);
        var fromThreadField = dispatcherType.GetField(
            "_dispatchers",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (fromThreadField?.GetValue(null) is System.Collections.IEnumerable dispatchers)
        {
            foreach (var d in dispatchers)
            {
                if (d is Dispatcher dispatcher && dispatcher.Thread.IsAlive)
                {
                    return dispatcher;
                }
            }
        }
#endif

        // No viable Dispatcher found. Do NOT fall back to Dispatcher.CurrentDispatcher —
        // that creates a pump-less Dispatcher on the injected CLR thread, causing all
        // InvokeAsync calls to queue work that is never executed.
        throw new InvalidOperationException(
            "[SnoopAgent] Could not locate a live WPF Dispatcher in the target process. " +
            "The target application may not have a WPF message loop running yet. " +
            "Retry injection after the application's main window is visible.");
    }
}
