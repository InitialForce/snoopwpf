namespace SnoopWPF.Agent.BrokerHost;

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Entry point for the broker-side MCP host.
/// </summary>
/// <remarks>
/// <para>
/// The broker owns the MCP stdio channel. Its very first statement is
/// <c>Console.SetOut(TextWriter.Null)</c> so that no stray writes from any library
/// ever corrupt the MCP framing on stdout. All diagnostic output should use stderr or
/// a log file.
/// </para>
/// <para>
/// The broker does NOT instantiate <c>AuditLogWriter</c> — audit logging is
/// target-only (see M1-13 / B-5). The broker only sees anonymised MCP tool calls;
/// the injected target writes audit entries because it is the component that actually
/// reads WPF state.
/// </para>
/// <para>
/// Downstream consumers (e.g. MC's <c>UiMcpHost</c>) add their own lifecycle tools
/// (e.g. <c>mc_launch</c>) on top of the 27-tool surface registered here. Pass a
/// callback to <see cref="BrokerOptions.OnTargetDisconnected"/> to hook disconnection events.
/// </para>
/// </remarks>
public static class BrokerHost
{
    /// <summary>
    /// Starts the broker MCP server on the provided transport and runs until
    /// <paramref name="ct"/> is cancelled or the transport closes.
    /// </summary>
    /// <param name="transport">
    /// The already-constructed MCP server transport (e.g. <c>StdioServerTransport</c>
    /// or <c>StreamServerTransport</c>). The broker will run its MCP surface on this
    /// transport.
    /// </param>
    /// <param name="opts">
    /// Broker configuration: pipe name for the target connection and an optional
    /// disconnection callback.
    /// </param>
    /// <param name="ct">Cancellation token; cancel to shut down the server gracefully.</param>
    /// <returns>A <see cref="Task"/> that completes when the server exits.</returns>
    /// <remarks>
    /// <para>
    /// IMPORTANT: <c>Console.SetOut(TextWriter.Null)</c> is called as the very first
    /// statement. The broker owns MCP stdio; any library that writes to stdout would
    /// corrupt the MCP framing. All diagnostic output must use stderr or a log file.
    /// </para>
    /// </remarks>
    public static async Task Start(
        ITransport transport,
        BrokerOptions opts,
        CancellationToken ct = default)
    {
        // -----------------------------------------------------------------------
        // FIRST STATEMENT: silence stdout so no stray writes corrupt MCP framing.
        // The broker owns the MCP stdio channel; all diagnostics go to stderr.
        // -----------------------------------------------------------------------
        Console.SetOut(TextWriter.Null);

        if (transport is null)
        {
            throw new ArgumentNullException(nameof(transport));
        }

        if (opts is null)
        {
            throw new ArgumentNullException(nameof(opts));
        }

        if (string.IsNullOrEmpty(opts.PipeName))
        {
            throw new ArgumentException("BrokerOptions.PipeName must not be empty.", nameof(opts));
        }

        // FX2-C3 (WC-C1): SessionToken is required for the nonce+HMAC handshake.
        // Without it the pump reads handshake bytes as PipeResponse frames, hits
        // JsonException, and silently fails every brokered call at wire level.
        if (string.IsNullOrEmpty(opts.SessionToken))
        {
            throw new ArgumentException(
                "BrokerOptions.SessionToken must not be empty. Pass the same hex-encoded token delivered to the target process.",
                nameof(opts));
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "snoop-wpf-broker",
                Version = version,
            },
        };

        // -----------------------------------------------------------------------
        // Connect to the target over the named pipe.
        // PipeConnection wraps a NamedPipeServerStream; the target process connects
        // as the pipe client after being spawned by BrokerTargetSpawner.Spawn.
        // Pass -1 to skip PID verification (target PID is not known in brokered mode
        // before the target connects).
        // -----------------------------------------------------------------------
        using var pipeConnection = new PipeConnection(opts.PipeName, expectedClientPid: -1);

        // FX2-C3: wait for the target to connect AND complete the nonce+HMAC handshake
        // BEFORE the proxy pump starts consuming frames. Without these two awaits the
        // pump reads handshake bytes as PipeResponse frames and kills itself silently.
        await pipeConnection.WaitForConnectionAsync(ct).ConfigureAwait(false);
        await pipeConnection.HandshakeAsync(opts.SessionToken, ct).ConfigureAwait(false);

        await using var proxy = new PipeSnoopInspectorProxy(pipeConnection);

        // Register disconnection notification before starting the pump.
        if (opts.OnTargetDisconnected is not null)
        {
            RegisterDisconnectCallback(proxy, opts.OnTargetDisconnected, ct);
        }

        proxy.StartPump();

        // -----------------------------------------------------------------------
        // Build DI container and register the 27 tool proxies.
        // NOTE: AuditLogWriter is intentionally NOT registered here.
        //       Audit is target-only (M1-13 / B-5). The broker only sees MCP calls;
        //       the injected target writes audit entries for actual WPF state access.
        // -----------------------------------------------------------------------
        var services = new ServiceCollection();
        services.AddBrokerToolProxies(proxy);
        var sp = services.BuildServiceProvider();

        // -----------------------------------------------------------------------
        // Run the MCP server until cancellation or transport close.
        // -----------------------------------------------------------------------
        await using var server = McpServer.Create(transport, serverOptions, serviceProvider: sp);
        await server.RunAsync(ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Registers a callback that fires when the pipe pump detects the target has
    /// disconnected. Uses a fire-and-forget task to avoid blocking the pump.
    /// </summary>
    private static void RegisterDisconnectCallback(
        PipeSnoopInspectorProxy proxy,
        Action onDisconnected,
        CancellationToken ct)
    {
        // Monitor the proxy in a background task: if it starts throwing SessionNotFound
        // on probe calls, the target has disconnected; fire the callback.
        _ = MonitorDisconnectAsync(proxy, onDisconnected, ct);
    }

    private static async Task MonitorDisconnectAsync(
        PipeSnoopInspectorProxy proxy,
        Action onDisconnected,
        CancellationToken ct)
    {
        try
        {
            // Wait a tick to let the pump start.
            await Task.Delay(100, ct).ConfigureAwait(false);

            // Poll until the proxy reports disconnected.
            while (true)
            {
                try
                {
                    await proxy.GetSessionInfoAsync(ct).ConfigureAwait(false);
                }
                catch (SnoopException ex) when (ex.Code == SnoopErrorCode.SessionNotFound)
                {
                    // Target disconnected.
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Broker shutting down (ct fired) — do not fire callback.
                    return;
                }
                catch
                {
                    // Any other error also means a genuine pipe disconnection.
                    break;
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            onDisconnected();
        }
        catch (OperationCanceledException)
        {
            // Broker shutting down (ct fired via Task.Delay) — do not fire callback.
        }
        catch
        {
            // Best-effort — do not propagate.
        }
    }
}
