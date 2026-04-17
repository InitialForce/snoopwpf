namespace SnoopWPF.SampleBroker;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.BrokerHost;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Minimal sample broker demonstrating <c>SnoopWPF.Agent.BrokerHost</c> consumption.
///
/// Launch paths:
///   (default)    Broker mode: spawns SnoopWPF.SampleApp as the target over a named pipe,
///                then starts the MCP stdio server proxying the 18-tool surface.
///   --smoke      Self-test: validate broker infrastructure, exit 0 on success.
///
/// This mirrors the shape MC's UiMcpHost will implement.
/// Lifecycle tools (sample_launch, sample_exit) are modelled as inline helpers here;
/// MC's UiMcpHost adds them as registered MCP tools on top of the BrokerHost core.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool smoke = Array.Exists(args, a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase));

        // -----------------------------------------------------------------------
        // FIRST: silence stdout — broker owns the MCP stdio channel.
        // -----------------------------------------------------------------------
        Console.SetOut(TextWriter.Null);

        if (smoke)
        {
            return RunSmokeTest();
        }

        // -----------------------------------------------------------------------
        // Normal broker mode: generate pipe/token, spawn target, run MCP server.
        // -----------------------------------------------------------------------
        string pipeName = "snoop-sample-broker-" + Guid.NewGuid().ToString("N")[..8];
        string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        string targetExe = ResolveSampleAppExe();

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, ev) =>
        {
            ev.Cancel = true;
            cts.Cancel();
        };

        Process? targetProcess = null;

        try
        {
            // -----------------------------------------------------------------------
            // sample_launch: spawn the target WPF process with brokered-mode args.
            // BrokerTargetSpawner drains target stdout/stderr so nothing leaks to broker's stdio.
            // -----------------------------------------------------------------------
            targetProcess = BrokerTargetSpawner.Spawn(
                exe: targetExe,
                args: Array.Empty<string>(),
                pipeName: pipeName,
                tokenHex: tokenHex);

            Console.Error.WriteLine(
                $"[SampleBroker] sample_launch: target PID={targetProcess.Id}");

            var opts = new BrokerOptions
            {
                PipeName = pipeName,
                SessionToken = tokenHex,
                OnTargetDisconnected = () =>
                {
                    Console.Error.WriteLine("[SampleBroker] Target disconnected — shutting down.");
                    cts.Cancel();
                },
            };

            var serverOptions = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "snoop-sample-broker", Version = "1.0.0" },
            };
            await using var transport = new StdioServerTransport(serverOptions);
            await BrokerHost.Start(transport, opts, cts.Token).ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SampleBroker] Fatal: {ex.Message}");
            return 2;
        }
        finally
        {
            // sample_exit: kill the target process on broker shutdown.
            try
            {
                if (targetProcess is not null && !targetProcess.HasExited)
                {
                    targetProcess.Kill();
                    Console.Error.WriteLine("[SampleBroker] sample_exit: target process killed.");
                }

                targetProcess?.Dispose();
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    // -------------------------------------------------------------------------
    // Smoke test — validates broker infrastructure without requiring a live WPF target.
    // Checks:
    //   1. BrokerOptions can be constructed with a valid pipe name.
    //   2. BrokerTargetSpawner.Spawn validates arguments (null checks).
    //   3. The sample target executable can be resolved (path check).
    // Exits 0 on pass, non-zero on failure.
    // -------------------------------------------------------------------------

    private static int RunSmokeTest()
    {
        Console.SetOut(Console.Error); // restore stderr for smoke output

        try
        {
            // 1. BrokerOptions construction.
            var pipeName = "snoop-smoke-" + Guid.NewGuid().ToString("N")[..8];
            var opts = new BrokerOptions { PipeName = pipeName };
            if (string.IsNullOrEmpty(opts.PipeName))
            {
                Console.Error.WriteLine("[smoke] FAIL: BrokerOptions.PipeName is empty after assignment.");
                return 1;
            }

            // 2. BrokerTargetSpawner null-guard.
            try
            {
                BrokerTargetSpawner.Spawn(exe: null!, args: Array.Empty<string>(), pipeName: "x", tokenHex: "y");
                Console.Error.WriteLine("[smoke] FAIL: BrokerTargetSpawner.Spawn should have thrown for null exe.");
                return 1;
            }
            catch (ArgumentNullException)
            {
                // Expected.
            }

            // 3. Sample target executable resolution (path existence check, best-effort).
            string targetExe = ResolveSampleAppExe();
            Console.Error.WriteLine($"[smoke] Resolved target exe: {targetExe}");

            Console.Error.WriteLine("[smoke] PASS: broker infrastructure validated.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[smoke] FAIL: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // -------------------------------------------------------------------------
    // Helper: resolve SnoopWPF.SampleApp.exe path next to this binary.
    // -------------------------------------------------------------------------

    private static string ResolveSampleAppExe()
    {
        string? thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (thisDir is not null)
        {
            string candidate = Path.Combine(thisDir, "SnoopWPF.SampleApp.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "SnoopWPF.SampleApp.exe";
    }
}
