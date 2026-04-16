// SnoopWPF.Agent.IntegrationTests/BrokerHostIntegrationTests.cs
// Integration tests for SnoopWPF.Agent.BrokerHost.
// M2-21 acceptance — filter: FullyQualifiedName~BrokerHost|FullyQualifiedName~BrokerTargetSpawner

namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.BrokerHost;

/// <summary>
/// Integration tests for <see cref="BrokerHost"/> and <see cref="BrokerTargetSpawner"/>.
/// </summary>
/// <remarks>
/// <para>
/// The broker + target round-trip over all 18 tools (M2-21 acceptance criterion) requires
/// a live WPF application and is marked <see cref="CategoryAttribute"/> MANUAL_VERIFICATION
/// because the full-round-trip path depends on M2-19 (brokered-mode consumer deliverables)
/// and the injection EXE being available on the CI agent.
/// </para>
/// <para>
/// Tests that CAN run without WPF are included here as normal [Test] methods.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class BrokerHostIntegrationTests
{
    // -------------------------------------------------------------------------
    // BrokerTargetSpawner — stdout does NOT appear on broker stdout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a child process that writes to stdout and verifies that output does NOT
    /// appear on the broker's captured stdout within 500 ms.
    ///
    /// This validates the drain-task invariant: broker stdout must remain clean for MCP
    /// framing even when the target process writes freely.
    /// </summary>
    [Test]
    public async Task BrokerTargetSpawner_TargetStdout_DoesNotAppearOnBrokerStdout()
    {
        // Capture broker stdout into a StringWriter.
        var brokerStdoutCapture = new StringBuilder();
        var capturedWriter = new StringWriter(brokerStdoutCapture);
        TextWriter originalOut = Console.Out;
        Console.SetOut(capturedWriter);

        try
        {
            // Spawn a child process that writes a known sentinel to stdout.
            // Use "dotnet --version" which always writes to stdout.
            string dotnetExe = GetDotnetExe();

            Process? process = null;
            try
            {
                process = BrokerTargetSpawner.Spawn(
                    exe: dotnetExe,
                    args: "--version",
                    pipeName: "broker-integ-test-" + Guid.NewGuid().ToString("N"),
                    tokenHex: "deadbeef");

                // Wait up to 500 ms for the child to exit.
                await Task.Delay(500).ConfigureAwait(false);

                try
                {
                    process.WaitForExit(500);
                }
                catch
                {
                    // Best-effort.
                }
            }
            finally
            {
                try
                {
                    process?.Kill();
                    process?.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            // Flush and read whatever ended up in the capture buffer.
            await capturedWriter.FlushAsync().ConfigureAwait(false);
            string brokerStdout = brokerStdoutCapture.ToString();

            // Assert: broker stdout is empty — no child output leaked through.
            Assert.That(
                brokerStdout,
                Is.Empty,
                "Child process stdout must not appear on broker stdout. " +
                "BrokerTargetSpawner.Spawn must drain the child's stdout silently.");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // -------------------------------------------------------------------------
    // Broker + target round-trip — MANUAL VERIFICATION
    // -------------------------------------------------------------------------

    /// <summary>
    /// MANUAL VERIFICATION — requires a live WPF application target.
    ///
    /// Full broker + target round-trip over all 18 MCP tools. Each tool call must
    /// succeed (non-error MCP response) when a target WPF process is connected.
    ///
    /// To run manually:
    ///   1. Start a WPF application (e.g. SnoopWPF.SampleApp).
    ///   2. Set SNOOP_TEST_PID environment variable to the target PID.
    ///   3. Run: dotnet test SnoopWPF.Agent.IntegrationTests --filter FullyQualifiedName~BrokerHost_RoundTrip_AllTools
    /// </summary>
    [Test]
    [Category("MANUAL_VERIFICATION")]
    [Ignore("Requires live WPF target — set SNOOP_TEST_PID and run manually (M2-19).")]
    public void BrokerHost_RoundTrip_AllTools_ManualVerification()
    {
        // This test is intentionally left as a stub.
        // Full round-trip coverage feeds M2-19 (brokered-mode consumer deliverables).
        // When M2-19 is implemented, this test should be updated to:
        //   1. Spawn BrokerTargetSpawner.Spawn(snoop-mcp.exe, ...) against the target PID.
        //   2. Start BrokerHost.Start(StdioServerTransport, opts) in a background task.
        //   3. Call all 18 MCP tools via the McpTestClient.
        //   4. Assert each tool returns a non-error response.
        Assert.Ignore("MANUAL_VERIFICATION: see test summary for instructions.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string GetDotnetExe()
    {
        string? mainExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(mainExe) && File.Exists(mainExe))
        {
            return mainExe;
        }

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is not null)
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                string full = Path.Combine(dir.Trim(), "dotnet.exe");
                if (File.Exists(full))
                {
                    return full;
                }

                full = Path.Combine(dir.Trim(), "dotnet");
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return "dotnet";
    }
}
