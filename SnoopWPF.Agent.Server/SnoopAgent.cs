namespace SnoopWPF.Agent.Server;

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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
            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath);
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
            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath);
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

            if (!string.IsNullOrEmpty(options.AuditLogPath))
            {
                handle.AuditWriter = new AuditLogWriter(options.AuditLogPath);
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
                    handle.IsStarted = true;
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
            handle.IsStarted = true;
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
            handle.IsStarted = true;
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
