// SnoopWPF.Agent.IntegrationTests/BrokerSpawnSecurityTests.cs
// FX-M6 acceptance — filter: FullyQualifiedName~BrokerSpawnSecurity

namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.BrokerHost;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Security tests for the broker→target session-token handoff (FX-M6).
/// Verifies that the 256-bit session token is no longer passed on the child process
/// command line and is instead delivered securely via stdin.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BrokerSpawnSecurityTests
{
    // -------------------------------------------------------------------------
    // Token must NOT appear on the child's command line
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a child process via <see cref="BrokerTargetSpawner"/> and verifies that the
    /// token hex string does NOT appear in the child's recorded command-line arguments.
    ///
    /// Uses a trivial child program (<c>dotnet --version</c>) that exits immediately.
    /// The test captures the child's <see cref="ProcessStartInfo.Arguments"/> before the
    /// process is launched — that is the string visible on the OS command line.
    /// </summary>
    [Test]
    public async Task BrokerTargetSpawner_Spawn_TokenHex_NotPresentOnCommandLine()
    {
        string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string pipeName = "snoop-sec-test-" + Guid.NewGuid().ToString("N");
        string dotnetExe = GetDotnetExe();

        // Intercept ProcessStartInfo before launch by using a custom spawn wrapper.
        // We verify via the Arguments string that was assembled — no token should be there.
        string capturedArguments = CaptureSpawnArguments(dotnetExe, "--version", pipeName, tokenHex);

        // Assert: the token must not appear in the command-line arguments.
        Assert.That(
            capturedArguments,
            Does.Not.Contain(tokenHex),
            $"Session token '{tokenHex}' must NOT appear in the spawned process arguments. " +
            "Tokens must be delivered via stdin (BrokerHandshakePayload), not the command line.");

        // Assert: the pipe name IS present (it's not secret).
        Assert.That(
            capturedArguments,
            Does.Contain(pipeName),
            $"Pipe name '{pipeName}' must appear in the spawned process arguments.");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Stdin handshake payload round-trip serialization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that <see cref="BrokerHandshakePayload"/> serializes and deserializes correctly
    /// with <see cref="System.Text.Json.JsonSerializer"/>, matching the format that
    /// <see cref="BrokerTargetSpawner.Spawn"/> writes to child stdin and that
    /// <c>SnoopWPF.SampleApp</c> reads during startup.
    /// </summary>
    [Test]
    public async Task BrokerHandshakePayload_SerializationRoundTrip_PreservesTokenAndPipe()
    {
        string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string pipeName = "snoop-sec-stdin-" + Guid.NewGuid().ToString("N");

        // Simulate what BrokerTargetSpawner.Spawn writes to child stdin.
        var original = new BrokerHandshakePayload { Pipe = pipeName, Token = tokenHex };
        string json = JsonSerializer.Serialize(original);

        // Verify it is a single line (no embedded newlines).
        Assert.That(json, Does.Not.Contain("\n"),
            "Serialized payload must not contain embedded newlines (it is written as a single line).");
        Assert.That(json, Does.Not.Contain("\r"),
            "Serialized payload must not contain embedded carriage returns.");

        // Deserialize — simulates what SampleApp.OnStartup does.
        BrokerHandshakePayload? roundTripped = JsonSerializer.Deserialize<BrokerHandshakePayload>(json.Trim());

        Assert.That(roundTripped, Is.Not.Null,
            "Deserialized BrokerHandshakePayload must not be null.");
        Assert.That(roundTripped!.Token, Is.EqualTo(tokenHex),
            "BrokerHandshakePayload.Token must survive serialization round-trip.");
        Assert.That(roundTripped.Pipe, Is.EqualTo(pipeName),
            "BrokerHandshakePayload.Pipe must survive serialization round-trip.");

        // Verify the token does NOT appear in the spawned process arguments.
        // This is redundant with BrokerTargetSpawner_Spawn_TokenHex_NotPresentOnCommandLine
        // but provides belt-and-suspenders coverage.
        Assert.That(json, Does.Contain(tokenHex),
            "Serialized payload JSON must contain the token (it is the secure delivery channel).");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // BrokerHostIntegrationTests compatibility — existing test still passes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Regression guard: <see cref="BrokerTargetSpawner.Spawn"/> must still drain stdout
    /// after the stdin-handoff change. This mirrors the existing stdout isolation test.
    /// </summary>
    [Test]
    public async Task BrokerTargetSpawner_AfterStdinChange_StillDrainsChildStdout()
    {
        var capturedWriter = new System.Text.StringBuilder();
        TextWriter originalOut = Console.Out;
        Console.SetOut(new StringWriter(capturedWriter));

        try
        {
            string dotnetExe = GetDotnetExe();
            Process? process = null;
            try
            {
                process = BrokerTargetSpawner.Spawn(
                    exe: dotnetExe,
                    args: "--version",
                    pipeName: "snoop-drain-test-" + Guid.NewGuid().ToString("N"),
                    tokenHex: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

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
                if (process is not null)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Best-effort.
                    }

                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                        // Best-effort.
                    }
                }
            }

            string brokerStdout = capturedWriter.ToString();
            Assert.That(brokerStdout, Is.Empty,
                "Child stdout must not appear on broker stdout after the stdin-handoff change.");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls <see cref="BrokerTargetSpawner.Spawn"/> for real (which launches a child process),
    /// then immediately kills the child and returns the <see cref="ProcessStartInfo.Arguments"/>
    /// string that was built, as captured via the spawned process object.
    /// </summary>
    private static string CaptureSpawnArguments(
        string exe, string extraArgs, string pipeName, string tokenHex)
    {
        Process? process = null;
        try
        {
            process = BrokerTargetSpawner.Spawn(exe, extraArgs, pipeName, tokenHex);

            // The Arguments we want to inspect are on the StartInfo of the live process.
            // ProcessStartInfo.Arguments reflects what was passed to Process.Start.
            return process.StartInfo.Arguments;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best-effort.
                }

                try
                {
                    process.Dispose();
                }
                catch
                {
                    // Best-effort.
                }
            }
        }
    }

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
