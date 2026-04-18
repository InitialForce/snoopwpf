namespace SnoopWPF.Agent.Server;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Engine.Audit;

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
        // M1-19: stdout held by MCP transport; see SWPF0001.
        // This MUST be the first statement: any Console.Write before this line leaks
        // onto the stdio MCP transport and corrupts the JSON-RPC framing.
        // NOTE: StartBrokered does NOT take over stdout — the broker owns its own stdio.
        Console.SetOut(TextWriter.Null);

        // FX4-C2-retry: delegate to shared helper (see bottom of class).
        SetStdioEncodingIfApplicable(options);

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

                // FX2-C2: propagate AllowSensitiveRetention from policy → engine options.
                // SessionPolicy.Create(Injection) clamps this to false; for CoLocated/Brokered
                // the caller's opts value flows through.
                AllowSensitiveRetention = policy.AllowSensitiveRetention,
                EnableAutomation = policy.EnableAutomation,
            };

            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: Application.Current,
                options: inspectorOptions,
                sessionPolicy: policy);

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

            // Wire audit log writer if requested (N1: AuditLogWriter production wiring).
            // FX6-D2: probe-write at construction; throws AuditUnwritable if path not writable
            // unless AllowAuditFallback=true, in which case it falls back to %LOCALAPPDATA%\SnoopWPF.Agent\audit\.
            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath, allowFallback: options.AllowAuditFallback);
            }

            // Auto-stop when the application exits.
            var app = Application.Current;
            if (app != null)
            {
                app.Exit += (_, _) => handle.Dispose();
            }

            // Run the server on the thread pool so we don't block the caller or the Dispatcher.
            // FX6-D1: pass the handle as IStartupFailureSink so startup exceptions surface.
            _ = Task.Run(
                () => RunServerAsync(inspector, options, policy, handle, (IStartupFailureSink)handle, cts.Token),
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

    /// <summary>
    /// Starts the SnoopWPF MCP agent in brokered mode.
    /// Unlike <see cref="StartCoLocated"/>, this overload does NOT redirect
    /// <see cref="Console.Out"/> to <see cref="TextWriter.Null"/> — the host broker
    /// owns its own stdio transport.
    /// </summary>
    /// <param name="app">The WPF application hosting the agent.</param>
    /// <param name="pipeName">Named-pipe name for the agent endpoint.</param>
    /// <param name="sessionToken">Session token for handshake authentication.</param>
    /// <param name="options">Optional configuration.</param>
    /// <returns>A <see cref="SnoopAgentHandle"/> that can be disposed to stop the server.</returns>
    public static SnoopAgentHandle StartBrokered(
        System.Windows.Application app,
        string pipeName,
        string sessionToken,
        SnoopAgentOptions? options = null)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (string.IsNullOrEmpty(pipeName))
        {
            throw new ArgumentException("pipeName must not be null or empty.", nameof(pipeName));
        }

        if (string.IsNullOrEmpty(sessionToken))
        {
            throw new ArgumentException("sessionToken must not be null or empty.", nameof(sessionToken));
        }

        // FX4-C2-retry: apply UTF-8 encoding for any stdio transport path (no-op for Pipe here,
        // but called unconditionally so future transport changes are automatically guarded).
        SetStdioEncodingIfApplicable(options);

        options ??= new SnoopAgentOptions();

        // In brokered mode, pipe name and session token are supplied by the caller.
        options = new SnoopAgentOptions
        {
            Transport = TransportMode.Pipe,
            PipeName = pipeName,
            SessionToken = sessionToken,
            TimeoutMs = options.TimeoutMs,
            EnableMutation = options.EnableMutation,
            EnableRedaction = options.EnableRedaction,
            AuditLogPath = options.AuditLogPath,
            AllowAuditFallback = options.AllowAuditFallback,
        };

        lock (Lock)
        {
            if (activeHandle != null)
            {
                throw new InvalidOperationException(
                    "SnoopAgent is already running. Dispose the existing handle before calling StartBrokered() again.");
            }

            var dispatcher = app.Dispatcher
                ?? throw new InvalidOperationException(
                    "SnoopAgent.StartBrokered() requires a valid WPF Application with a Dispatcher.");

            // Brokered mode uses SessionMode.Brokered — passes opts through unchanged (owned app).
            var policy = SessionPolicy.Create(SessionMode.Brokered, options);

            var inspectorOptions = new SnoopInspectorOptions
            {
                TimeoutMs = options.TimeoutMs,
                EnableMutation = policy.EnableMutation,
                EnableRedaction = policy.EnableRedaction,

                // FX2-C2: propagate AllowSensitiveRetention + EnableAutomation from policy.
                AllowSensitiveRetention = policy.AllowSensitiveRetention,
                EnableAutomation = policy.EnableAutomation,
            };

            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: app,
                options: inspectorOptions,
                sessionPolicy: policy);

            var cts = new CancellationTokenSource();
            var handle = new SnoopAgentHandle(cts, inspector, policy);
            handle.PipeName = pipeName;
            handle.SessionToken = sessionToken;
            activeHandle = handle;

            // Wire audit log writer if requested (N1: AuditLogWriter production wiring).
            // FX6-D2: probe-write at construction.
            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath, allowFallback: options.AllowAuditFallback);
            }

            // Auto-stop when the application exits.
            app.Exit += (_, _) => handle.Dispose();

            // Run the brokered reconnect loop on the thread pool.
            // NOTE: unlike StartCoLocated, self-tests are skipped here because the WPF dispatcher
            // and HwndSource may not yet be fully initialised at StartBrokered call time.
            _ = Task.Run(() => RunBrokeredAsync(inspector, options, policy, pipeName, sessionToken, handle.AuditWriter, handle, (IStartupFailureSink)handle, cts.Token));

            return handle;
        }
    }

    /// <summary>
    /// Starts the SnoopWPF agent in brokered-client mode: the WPF target connects to a
    /// <b>broker-owned</b> named pipe as the <see cref="System.IO.Pipes.NamedPipeClientStream"/>
    /// client, completes the nonce+HMAC handshake, and serves <c>PipeRequest</c> frames against
    /// a local <c>SnoopInspector</c>. This is the counterpart to
    /// <c>SnoopWPF.Agent.BrokerHost.BrokerHost.Start</c>, which owns the
    /// <see cref="System.IO.Pipes.NamedPipeServerStream"/> on the broker side.
    /// </summary>
    /// <remarks>
    /// Use this overload when the target WPF process is spawned by a broker (e.g.
    /// MotionCatalyst launched by <c>UiMcpHost</c> with <c>--ui-mcp-pipe=&lt;name&gt;</c>)
    /// and the broker exposes the 18 <c>wpf_*</c> tool proxies on its own MCP surface.
    /// Use <see cref="StartBrokered"/> instead when the WPF target owns the pipe server and
    /// runs its own MCP server on the pipe.
    /// </remarks>
    /// <param name="app">The WPF application hosting the agent.</param>
    /// <param name="pipeName">Named-pipe name the broker created (target connects as client).</param>
    /// <param name="sessionToken">Hex-encoded session token for HMAC-SHA256 handshake.</param>
    /// <param name="options">Optional configuration. Mutation/automation tiers honored.</param>
    /// <returns>A <see cref="SnoopAgentHandle"/> that can be disposed to stop the agent.</returns>
    public static SnoopAgentHandle StartBrokeredClient(
        System.Windows.Application app,
        string pipeName,
        string sessionToken,
        SnoopAgentOptions? options = null)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (string.IsNullOrEmpty(pipeName))
        {
            throw new ArgumentException("pipeName must not be null or empty.", nameof(pipeName));
        }

        if (string.IsNullOrEmpty(sessionToken))
        {
            throw new ArgumentException("sessionToken must not be null or empty.", nameof(sessionToken));
        }

        // FX4-C2-retry: apply UTF-8 encoding for any stdio transport path.
        SetStdioEncodingIfApplicable(options);

        options ??= new SnoopAgentOptions();

        lock (Lock)
        {
            if (activeHandle != null)
            {
                throw new InvalidOperationException(
                    "SnoopAgent is already running. Dispose the existing handle before calling StartBrokeredClient() again.");
            }

            var dispatcher = app.Dispatcher
                ?? throw new InvalidOperationException(
                    "SnoopAgent.StartBrokeredClient() requires a valid WPF Application with a Dispatcher.");

            var policy = SessionPolicy.Create(SessionMode.Brokered, options);

            var inspectorOptions = new SnoopInspectorOptions
            {
                TimeoutMs = options.TimeoutMs,
                EnableMutation = policy.EnableMutation,
                EnableRedaction = policy.EnableRedaction,
                AllowSensitiveRetention = policy.AllowSensitiveRetention,
                EnableAutomation = policy.EnableAutomation,
            };

            var inspector = new SnoopInspector(
                dispatcher,
                rootTarget: app,
                options: inspectorOptions,
                sessionPolicy: policy);

            var cts = new CancellationTokenSource();
            var handle = new SnoopAgentHandle(cts, inspector, policy);
            handle.PipeName = pipeName;
            handle.SessionToken = sessionToken;
            activeHandle = handle;

            // FX6-D2: probe-write at construction.
            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath, allowFallback: options.AllowAuditFallback);
            }

            app.Exit += (_, _) => handle.Dispose();

            byte[] tokenBytes = Encoding.UTF8.GetBytes(sessionToken);
            var pipeClient = new SnoopWPF.Agent.Injection.PipeAgentServer(pipeName, tokenBytes, inspector);
            // PipeAgentServer holds its own copy; zero our buffer as defense-in-depth.
            Array.Clear(tokenBytes, 0, tokenBytes.Length);

            IStartupFailureSink brokeredClientSink = (IStartupFailureSink)handle;
            _ = Task.Run(async () =>
            {
                try
                {
                    // FX6-D3: record StartedAt for uptime computation in wpf_diagnostics.
                    handle.IsStarted = true;
                    handle.StartedAt = DateTimeOffset.UtcNow;
                    await pipeClient.RunAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown.
                }
                catch (Exception ex)
                {
                    // FX6-D1: route to failure sink.
                    handle.IsStarted = false;
                    brokeredClientSink.OnStartupFailed(ex);
                    Trace.TraceError(
                        "SnoopWPF.Agent (BrokeredClient) error: {0}: {1}",
                        ex.GetType().FullName, ex.Message);
                }
                finally
                {
                    pipeClient.Dispose();
                }
            });

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

    /// <summary>
    /// Starts the SnoopWPF agent in Mode 2 (warm-attach) pipe SERVER role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Mode 2 the WPF target creates the named pipe and waits for a broker to connect
    /// as the pipe CLIENT. This is the symmetric counterpart to
    /// <see cref="StartBrokeredClient"/>, where the broker owns the pipe server and the
    /// target connects as the client.
    /// </para>
    /// <para>
    /// The method returns immediately with a <see cref="BrokeredServerHandle"/> whose
    /// <see cref="BrokeredServerHandle.PipeName"/> and
    /// <see cref="BrokeredServerHandle.SessionToken"/> the caller must embed in a manifest
    /// file (bd-1a9.24) so the broker can discover and connect to the pipe.
    /// </para>
    /// <para>
    /// The pipe is created with a hardened DACL via <see cref="NamedPipeServerStreamAcl.Create"/>:
    /// a protected <see cref="System.IO.Pipes.PipeSecurity"/> with
    /// <c>SetAccessRuleProtection(isProtected: true, preserveInheritance: false)</c> and a
    /// single ALLOW ACE for the current user SID (FullControl).  No inherited ACEs are present.
    /// A broker running as a different user will receive an <c>ACCESS_DENIED</c> error when
    /// calling <c>CreateFile</c> on the pipe.
    /// </para>
    /// <para>
    /// The HMAC handshake is identical to the one used by <see cref="StartBrokered"/> —
    /// the server sends a random 16-byte nonce, the client responds with
    /// <c>HMACSHA256(key=sessionTokenBytes, data=nonce)</c> and a matching protocol version.
    /// The session token is never transmitted over the pipe.
    /// </para>
    /// </remarks>
    /// <param name="settings">
    /// Configuration for the pipe server.  Pipe name and session token are auto-generated
    /// when not specified in <paramref name="settings"/>.
    /// </param>
    /// <param name="ct">
    /// Cancellation token.  Cancelling stops the listener without waiting for a broker to connect.
    /// </param>
    /// <returns>
    /// A <see cref="BrokeredServerHandle"/> that exposes the <c>PipeName</c> and
    /// <c>SessionToken</c> for manifest writing.  Dispose the handle to stop listening.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    public static Task<BrokeredServerHandle> StartBrokeredServerAsync(
        BrokeredServerSettings settings,
        CancellationToken ct = default)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        // Generate pipe name using the canonical Mode 2 format when the caller did not supply one.
        string pipeName;
        if (!string.IsNullOrEmpty(settings.PipeName))
        {
            pipeName = settings.PipeName!;
        }
        else
        {
            // Format: motioncatalyst-mcp-<sid>-<pid>-<startTimeTicks>
            // WindowsIdentity.GetCurrent().User is guaranteed non-null on Windows;
            // SDDL form (e.g. S-1-5-21-...) is used so the name is safe in a file path.
            string sid;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                sid = identity.User?.ToString() ?? "unknown";
            }

            int pid = Environment.ProcessId;
            long startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            pipeName = $"motioncatalyst-mcp-{sid}-{pid}-{startTicks}";
        }

        // Generate a 256-bit session token when the caller did not supply one.
        string sessionToken = !string.IsNullOrEmpty(settings.SessionToken)
            ? settings.SessionToken!
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        // Build a linked CTS so that either external cancellation or handle.Dispose()
        // can stop the listener.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Apply lease duration as an additional timeout if specified.
        if (settings.LeaseDuration.HasValue)
        {
            linkedCts.CancelAfter(settings.LeaseDuration.Value);
        }

        // Start the listener task.  It runs on the thread pool so the caller is not blocked.
        var listenerTask = Task.Run(
            () => RunBrokeredServerListenerAsync(pipeName, sessionToken, settings, linkedCts.Token),
            linkedCts.Token);

        // Write the session manifest AFTER the pipe listener is started (R4 invariant: pipe-first).
        // ManifestHandle disposal is wired to linkedCts cancellation so the manifest file is
        // cleaned up when the handle is disposed or the lease expires.
        ManifestHandle? manifestHandle = null;
        try
        {
            manifestHandle = SessionManifestWriter.Write(pipeName, sessionToken);
        }
        catch (Exception ex)
        {
            // Manifest write failure is non-fatal: the pipe is already listening.
            // Log and continue so the broker can still connect (it will fail manifest validation).
            Trace.TraceWarning(
                "SnoopWPF.Agent Mode 2: session manifest write failed (non-fatal): {0}: {1}",
                ex.GetType().Name, ex.Message);
        }

        var handle = new BrokeredServerHandle(pipeName, sessionToken, linkedCts, listenerTask, manifestHandle);

        Trace.TraceInformation(
            "SnoopWPF.Agent Mode 2 pipe server started: pipe='{0}'.", pipeName);

        return Task.FromResult(handle);
    }

    /// <summary>
    /// Background listener for Mode 2 (warm-attach) server role.
    /// Delegates pipe creation and handshake to <see cref="BrokerPipeServer"/>, which
    /// applies a hardened DACL (protected, single ALLOW ACE for current user SID, no
    /// inherited ACEs) and enforces the ALREADY_ATTACHED constraint (R7).
    /// </summary>
    private static async Task RunBrokeredServerListenerAsync(
        string pipeName,
        string sessionToken,
        BrokeredServerSettings settings,
        CancellationToken ct)
    {
        _ = settings; // AgentOptions forwarded in bd-1a9.23 when MCP session is wired.

        // BrokerPipeServer creates the pipe with:
        //   - NamedPipeServerStreamAcl.Create (security set atomically at creation)
        //   - PipeSecurity with SetAccessRuleProtection(true, false) — no inherited ACEs
        //   - Single ALLOW ACE for current user SID (FullControl)
        // After the first authenticated client connects, it starts an ALREADY_ATTACHED
        // guard loop that rejects any subsequent connection with a structured error frame.
        using var brokerPipeServer = new BrokerPipeServer(pipeName, sessionToken);

        using var authenticatedPipe = await brokerPipeServer
            .AcceptAuthenticatedClientAsync(ct)
            .ConfigureAwait(false);

        if (authenticatedPipe is null)
        {
            // Handshake failed or cancelled — listener exits cleanly.
            Trace.TraceInformation(
                "SnoopWPF.Agent Mode 2 pipe server: listener exited (handshake failed or cancelled).");
            return;
        }

        Trace.TraceInformation("SnoopWPF.Agent Mode 2 pipe server: broker authenticated. Handing off to MCP session.");

        // The authenticated pipe stream is now ready. Dispose it so the OS handle is
        // released; RunBrokeredPipeAsync (bd-1a9.23) will re-create the listener on the
        // same pipe name for the reconnect loop.
        // NOTE: Mode 2 production wiring (inspector handoff) is completed in bd-1a9.23.
        Trace.TraceInformation(
            "SnoopWPF.Agent Mode 2 pipe server: handshake complete (MCP session wiring deferred to bd-1a9.23).");
    }

    private static async Task RunServerAsync(
        SnoopInspector inspector,
        SnoopAgentOptions options,
        SessionPolicy policy,
        SnoopAgentHandle handle,
        IStartupFailureSink failureSink,
        CancellationToken ct)
    {
        try
        {
            // Boot-sequence step 5 (PRD §4.2): run startup self-tests BEFORE the MCP loop.
            // These are placed here (on the thread-pool task) so they run after Console.SetOut
            // has been redirected (M1-19) and before any MCP messages are accepted.
            SelfTest.UnsafeAccessorBindings();
            SelfTest.HwndSourcePresent();

            // FX6-D1: mark the handle as started before entering the MCP loop.
            // FX6-D3: record StartedAt for uptime computation in wpf_diagnostics.
            handle.IsStarted = true;
            handle.StartedAt = DateTimeOffset.UtcNow;
            await McpServerSetup.RunServerAsync(inspector, options, policy, handle, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — swallow.
        }
        catch (Exception ex)
        {
            // FX6-D1: route to failure sink (writes framed MCP error to stderr + sets StartupException).
            handle.IsStarted = false;
            failureSink.OnStartupFailed(ex);
            Trace.TraceError("SnoopWPF.Agent MCP server error: {0}", ex.Message);
        }
    }

    private static async Task RunBrokeredAsync(
        SnoopInspector inspector,
        SnoopAgentOptions options,
        SessionPolicy policy,
        string pipeName,
        string sessionTokenHex,
        SnoopWPF.Agent.Engine.Audit.AuditLogWriter? auditWriter,
        SnoopAgentHandle handle,
        IStartupFailureSink failureSink,
        CancellationToken ct)
    {
        try
        {
            // Boot-sequence self-tests (same as CoLocated path).
            SelfTest.UnsafeAccessorBindings();
            SelfTest.HwndSourcePresent();

            // FX6-D1: mark the handle as started before entering the brokered loop.
            // FX6-D3: record StartedAt for uptime computation in wpf_diagnostics.
            handle.IsStarted = true;
            handle.StartedAt = DateTimeOffset.UtcNow;
            await McpServerSetup.RunBrokeredPipeAsync(inspector, policy, pipeName, sessionTokenHex, ct, auditWriter, options)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow.
        }
        catch (Exception ex)
        {
            // FX6-D1: route to failure sink.
            handle.IsStarted = false;
            failureSink.OnStartupFailed(ex);
            Trace.TraceError("SnoopWPF.Agent MCP server (Brokered) error: {0}", ex.Message);
        }
    }

    // FX4-C2-retry: shared UTF-8 stdio setup, called as the first statement of every
    // public Start* entry point that may drive a stdio MCP transport.
    //
    // Set UTF-8 on stdin/stdout for stdio MCP transport so non-ASCII element names
    // (Cyrillic, CJK, emoji) don't get corrupted on legacy Windows locales.
    // Guarded by IOException for detached GUI hosts where Console is not attached.
    private static void SetStdioEncodingIfApplicable(SnoopAgentOptions? options)
    {
        var transport = (options ?? new SnoopAgentOptions()).Transport;
        if (transport != TransportMode.Stdio)
        {
            return;
        }

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Detached GUI host — no console handle. Non-fatal.
        }
    }
}
