namespace SnoopWPF.Agent.InjectionTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Live injection tests that spawn a real WPF sample app, inject snoop-mcp into it,
/// and verify that core tool calls work end-to-end.
///
/// These tests are marked <c>[Category("RequiresInjection")]</c> and are ignored by default
/// because they require a visible WPF process, CreateRemoteThread injection capability,
/// and built output for snoop-mcp.exe and the SampleApp.
///
/// To run locally:
///   dotnet test --filter "Category=RequiresInjection"
///
/// In CI, add Add-MpPreference -ExclusionPath $env:GITHUB_WORKSPACE before running.
/// </summary>
[TestFixture]
[Category("RequiresInjection")]
[Ignore("RequiresInjection: run manually with a visible WPF desktop. Set RequiresInjection env var to enable.")]
public sealed class LiveInjectionTests
{
    private static readonly string SolutionRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string SnoopMcpExe =>
        Path.Combine(SolutionRoot, "SnoopWPF.Agent.Host", "bin", "Debug", "net8.0-windows", "snoop-mcp.exe");

    private static string SampleAppExe =>
        Path.Combine(SolutionRoot, "Samples", "SnoopWPF.SampleApp", "bin", "Debug", "net8.0-windows",
            "SnoopWPF.SampleApp.exe");

    private Process? sampleAppProcess;

    [SetUp]
    public void SetUp()
    {
        Assert.That(
            File.Exists(SnoopMcpExe),
            Is.True,
            $"snoop-mcp.exe not found at {SnoopMcpExe}. Build the solution first.");

        Assert.That(
            File.Exists(SampleAppExe),
            Is.True,
            $"SampleApp not found at {SampleAppExe}. Build the solution first.");
    }

    [TearDown]
    public void TearDown()
    {
        if (this.sampleAppProcess != null && !this.sampleAppProcess.HasExited)
        {
            try
            {
                this.sampleAppProcess.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Best-effort cleanup.
            }
        }

        this.sampleAppProcess?.Dispose();
        this.sampleAppProcess = null;
    }

    [Test]
    public async Task LiveInject_SampleApp_GetSessionInfo_ReturnsMatchingPid()
    {
        // Start the sample app (--no-agent means it doesn't self-inject).
        this.sampleAppProcess = Process.Start(new ProcessStartInfo
        {
            FileName = SampleAppExe,
            Arguments = "--no-agent",
            UseShellExecute = true, // Needs a visible desktop.
            CreateNoWindow = false,
        })!;

        Assert.That(this.sampleAppProcess, Is.Not.Null);
        int targetPid = this.sampleAppProcess.Id;

        // Wait for the sample app to initialise.
        await Task.Delay(2000).ConfigureAwait(false);
        Assert.That(this.sampleAppProcess.HasExited, Is.False, "Sample app exited unexpectedly.");

        // Run snoop-mcp with stdio transport, targeting the sample app by PID.
        using var snoopMcp = new Process();
        snoopMcp.StartInfo = new ProcessStartInfo
        {
            FileName = SnoopMcpExe,
            Arguments = $"--pid {targetPid} --transport stdio",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        snoopMcp.Start();

        try
        {
            // Send MCP initialize + tool call via stdin.
            string initMsg = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}\n";
            await snoopMcp.StandardInput.WriteAsync(initMsg).ConfigureAwait(false);
            await snoopMcp.StandardInput.FlushAsync().ConfigureAwait(false);

            // Read response lines (non-blocking with timeout).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? initResponse = null;
            while (!cts.IsCancellationRequested)
            {
                string? line = await snoopMcp.StandardOutput.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.Contains("\"result\"") && line.Contains("\"serverInfo\""))
                {
                    initResponse = line;
                    break;
                }
            }

            Assert.That(initResponse, Is.Not.Null, "MCP initialize response not received.");

            // Call wpf_get_session_info.
            string toolCallMsg =
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"wpf_get_session_info\",\"arguments\":{}}}\n";
            await snoopMcp.StandardInput.WriteAsync(toolCallMsg).ConfigureAwait(false);
            await snoopMcp.StandardInput.FlushAsync().ConfigureAwait(false);

            string? toolResponse = null;
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!cts2.IsCancellationRequested)
            {
                string? line = await snoopMcp.StandardOutput.ReadLineAsync().WaitAsync(cts2.Token).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.Contains("\"id\":2") && line.Contains("\"result\""))
                {
                    toolResponse = line;
                    break;
                }
            }

            Assert.That(toolResponse, Is.Not.Null, "wpf_get_session_info response not received.");

            // Parse response and verify PID.
            using var doc = JsonDocument.Parse(toolResponse!);
            var root = doc.RootElement;

            // MCP content is in result.content[0].text (JSON string).
            string contentText = root
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;

            using var sessionDoc = JsonDocument.Parse(contentText);
            int reportedPid = sessionDoc.RootElement.GetProperty("pid").GetInt32();

            Assert.That(reportedPid, Is.EqualTo(targetPid),
                $"Reported PID {reportedPid} must match sample app PID {targetPid}.");
        }
        finally
        {
            try
            {
                snoopMcp.StandardInput.Close();
                await snoopMcp.WaitForExitAsync(new CancellationTokenSource(3000).Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try { snoopMcp.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
        }
    }

    [Test]
    public async Task LiveInject_SampleApp_GetWindows_ReturnsAtLeastOneWindow()
    {
        // Start the sample app (--no-agent means it doesn't self-inject).
        this.sampleAppProcess = Process.Start(new ProcessStartInfo
        {
            FileName = SampleAppExe,
            Arguments = "--no-agent",
            UseShellExecute = true,
            CreateNoWindow = false,
        })!;

        Assert.That(this.sampleAppProcess, Is.Not.Null);
        int targetPid = this.sampleAppProcess.Id;

        // Wait for the sample app to initialise.
        await Task.Delay(2000).ConfigureAwait(false);
        Assert.That(this.sampleAppProcess.HasExited, Is.False, "Sample app exited unexpectedly.");

        // Run snoop-mcp with stdio transport, targeting the sample app by PID.
        using var snoopMcp = new Process();
        snoopMcp.StartInfo = new ProcessStartInfo
        {
            FileName = SnoopMcpExe,
            Arguments = $"--pid {targetPid} --transport stdio",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        snoopMcp.Start();

        try
        {
            // Send MCP initialize + tool call via stdin.
            string initMsg = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}\n";
            await snoopMcp.StandardInput.WriteAsync(initMsg).ConfigureAwait(false);
            await snoopMcp.StandardInput.FlushAsync().ConfigureAwait(false);

            // Read response lines (non-blocking with timeout).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? initResponse = null;
            while (!cts.IsCancellationRequested)
            {
                string? line = await snoopMcp.StandardOutput.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.Contains("\"result\"") && line.Contains("\"serverInfo\""))
                {
                    initResponse = line;
                    break;
                }
            }

            Assert.That(initResponse, Is.Not.Null, "MCP initialize response not received.");

            // Call wpf_get_windows.
            string toolCallMsg =
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"wpf_get_windows\",\"arguments\":{\"includeHidden\":false}}}\n";
            await snoopMcp.StandardInput.WriteAsync(toolCallMsg).ConfigureAwait(false);
            await snoopMcp.StandardInput.FlushAsync().ConfigureAwait(false);

            string? toolResponse = null;
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!cts2.IsCancellationRequested)
            {
                string? line = await snoopMcp.StandardOutput.ReadLineAsync().WaitAsync(cts2.Token).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.Contains("\"id\":2") && line.Contains("\"result\""))
                {
                    toolResponse = line;
                    break;
                }
            }

            Assert.That(toolResponse, Is.Not.Null, "wpf_get_windows response not received.");

            // Parse response and verify at least one window is returned.
            using var doc = JsonDocument.Parse(toolResponse!);
            var root = doc.RootElement;

            // MCP content is in result.content[0].text (JSON string).
            string contentText = root
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()!;

            // contentText is either a JSON array of window objects or a JSON object wrapping them.
            // Try parsing as an array first; if it's an object, look for a windows property.
            using var windowsDoc = JsonDocument.Parse(contentText);
            JsonElement windowsArray;
            if (windowsDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                windowsArray = windowsDoc.RootElement;
            }
            else
            {
                // Some tool responses wrap the array: { "windows": [...] }
                windowsArray = windowsDoc.RootElement.GetProperty("windows");
            }

            Assert.That(windowsArray.GetArrayLength(), Is.GreaterThan(0),
                "wpf_get_windows must return at least one window for the running SampleApp.");
        }
        finally
        {
            try
            {
                snoopMcp.StandardInput.Close();
                await snoopMcp.WaitForExitAsync(new CancellationTokenSource(3000).Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try { snoopMcp.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
        }
    }
}
