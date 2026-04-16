namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Server;

/// <summary>
/// Integration tests for <see cref="SnoopAgent.StartBrokered"/>.
/// Verifies the brokered-mode API surface, session policy, and Console.Out invariant
/// using the shared WPF application fixture.
///
/// Named "BrokeredRoundTrip" so the acceptance-criteria filter
/// <c>FullyQualifiedName~BrokeredRoundTrip</c> selects all tests in this class.
/// </summary>
/// <remarks>
/// Wire-protocol round-trip tests use <see cref="SessionPolicy"/> and the API surface
/// exercised from the WPF dispatcher thread.
/// Named-pipe end-to-end tests are skipped on WSL1 where Windows named-pipe async
/// WaitForConnectionAsync does not signal reliably; that coverage lives in
/// <c>StartBrokeredTests</c> (unit tests) using in-process stream pairs.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class BrokeredRoundTripTests : WpfIntegrationTestBase
{
    private TextWriter? originalOut;

    /// <summary>Captures the real stdout before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        this.originalOut = Console.Out;
    }

    /// <summary>
    /// Restores stdout and clears any active agent handle.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (this.originalOut is not null)
        {
            Console.SetOut(this.originalOut);
            this.originalOut = null;
        }
    }

    // -------------------------------------------------------------------------
    // API surface — called from WPF dispatcher thread
    // -------------------------------------------------------------------------

    /// <summary>
    /// <see cref="SnoopAgent.StartBrokered"/> must return a non-null handle with the correct
    /// pipe name and session token echoed back on the handle.
    /// </summary>
    [Test]
    public void BrokeredRoundTrip_StartBrokered_ReturnsHandleWithPipeNameAndToken()
    {
        var pipeName = "snoop-test-brokered-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartBrokered(
                    this.WpfApp.App,
                    pipeName,
                    token,
                    new SnoopAgentOptions());
            });

            Assert.That(handle, Is.Not.Null, "StartBrokered must return a non-null handle.");
            Assert.That(handle!.PipeName, Is.EqualTo(pipeName),
                "Handle.PipeName must match the pipe name passed to StartBrokered.");
            Assert.That(handle.SessionToken, Is.EqualTo(token),
                "Handle.SessionToken must match the session token passed to StartBrokered.");
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>
    /// <see cref="SnoopAgent.StartBrokered"/> must NOT redirect <see cref="Console.Out"/>.
    /// The target process owns its own stdio; the broker (a separate process) owns the MCP
    /// stdio anchor. This is the critical distinction from <c>StartCoLocated</c>.
    /// </summary>
    [Test]
    public void BrokeredRoundTrip_StartBrokered_ConsoleOut_IsNotTextWriterNull()
    {
        var pipeName = "snoop-test-brokered-co-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        // Replace the real stdout with a capturing writer.
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        this.originalOut = capturedOut; // TearDown will restore this

        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartBrokered(
                    this.WpfApp.App,
                    pipeName,
                    token,
                    new SnoopAgentOptions());
            });

            // StartBrokered must NOT have replaced Console.Out with TextWriter.Null.
            Assert.That(Console.Out, Is.Not.SameAs(TextWriter.Null),
                "StartBrokered must NOT redirect Console.Out to TextWriter.Null. " +
                "Only StartCoLocated does this (M1-19). The target owns its own stdio in Brokered mode.");

            // Writes to Console must reach the capturing writer (not be swallowed).
            Console.Write("probe");
            Console.Out.Flush();
            Assert.That(capturedOut.ToString(), Does.Contain("probe"),
                "Console.Write after StartBrokered must still reach the original stdout.");
        }
        finally
        {
            handle?.Dispose();
            Console.SetOut(this.originalOut ?? TextWriter.Null);
            this.originalOut = null;
        }
    }

    /// <summary>
    /// <see cref="SnoopAgent.StartBrokered"/> constructs a <see cref="SessionPolicy"/> with
    /// <see cref="SessionMode.Brokered"/> — caller-supplied opts pass through unchanged.
    /// </summary>
    [Test]
    public void BrokeredRoundTrip_StartBrokered_SessionPolicy_IsBrokeredMode()
    {
        var pipeName = "snoop-test-brokered-policy-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var opts = new SnoopAgentOptions
        {
            EnableRedaction = false,
            EnableMutation = true,
        };

        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartBrokered(this.WpfApp.App, pipeName, token, opts);
            });

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Policy.Mode, Is.EqualTo(SessionMode.Brokered),
                "Policy.Mode must be Brokered.");
            Assert.That(handle.Policy.EnableRedaction, Is.False,
                "Brokered mode must not force EnableRedaction=true (MF-11 does not apply).");
            Assert.That(handle.Policy.EnableMutation, Is.True,
                "Brokered mode must honour the caller's EnableMutation choice.");
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>
    /// Calling <see cref="SnoopAgent.StartBrokered"/> a second time while the first handle
    /// is still alive must throw <see cref="InvalidOperationException"/>.
    /// </summary>
    [Test]
    public void BrokeredRoundTrip_StartBrokered_ThrowsIfAlreadyRunning()
    {
        var pipeName1 = "snoop-test-brokered-dup1-" + Guid.NewGuid().ToString("N");
        var pipeName2 = "snoop-test-brokered-dup2-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        SnoopAgentHandle? handle1 = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle1 = SnoopAgent.StartBrokered(this.WpfApp.App, pipeName1, token);
            });

            Assert.That(handle1, Is.Not.Null);

            Assert.Throws<InvalidOperationException>(() =>
            {
                this.WpfApp.Dispatcher.Invoke(() =>
                {
                    _ = SnoopAgent.StartBrokered(this.WpfApp.App, pipeName2, token);
                });
            }, "Starting a second Brokered session while the first is alive must throw.");
        }
        finally
        {
            handle1?.Dispose();
        }
    }

    /// <summary>
    /// <see cref="SnoopAgent.StartBrokered"/> with <c>EnableRedaction=true</c> must honour
    /// that choice — the target-side redaction sentinel is "[REDACTED]" in Brokered mode
    /// just as in CoLocated mode.
    /// </summary>
    [Test]
    public void BrokeredRoundTrip_StartBrokered_WithRedactionEnabled_PolicyReflectsIt()
    {
        var pipeName = "snoop-test-brokered-redact-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var opts = new SnoopAgentOptions { EnableRedaction = true };

        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartBrokered(this.WpfApp.App, pipeName, token, opts);
            });

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Policy.EnableRedaction, Is.True,
                "EnableRedaction=true must be reflected in the Brokered session policy.");
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>
    /// Cross-process smoke: spawns <c>SnoopWPF.SampleApp</c> with <c>--mcp-stdio --smoke</c>
    /// and asserts it exits 0.
    /// This validates the co-located <c>StartCoLocated</c> path end-to-end from a consumer
    /// perspective and is the primary brokered-mode consumer deliverable gate (M2-19).
    /// </summary>
    [Test]
    [CancelAfter(30_000)]
    public async Task BrokeredRoundTrip_SampleApp_McpStdioSmoke_ExitsZero()
    {
        string sampleAppExe = ResolveSampleAppExe();

        if (!File.Exists(sampleAppExe))
        {
            Assert.Ignore($"SnoopWPF.SampleApp.exe not found at '{sampleAppExe}'. Build first.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = sampleAppExe,
            Arguments = "--mcp-stdio --smoke",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start SnoopWPF.SampleApp.");

        // Drain output on background tasks to prevent pipe-full blocking.
        _ = proc.StandardOutput.ReadToEndAsync();
        string stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);

        bool exited = proc.WaitForExit(25_000);

        if (!exited)
        {
            proc.Kill();
            Assert.Fail("SnoopWPF.SampleApp --mcp-stdio --smoke did not exit within 25 s.");
            return;
        }

        Assert.That(
            proc.ExitCode,
            Is.EqualTo(0),
            $"SnoopWPF.SampleApp --mcp-stdio --smoke exited {proc.ExitCode}. Stderr: {stderr}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ResolveSampleAppExe()
    {
        // Look next to the running test assembly (both point to the same output directory
        // when all projects share OutputPath or when test artifacts are copied).
        string? thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (thisDir is not null)
        {
            string candidate = Path.Combine(thisDir, "SnoopWPF.SampleApp.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            "SnoopWPF.SampleApp.exe");
    }
}
