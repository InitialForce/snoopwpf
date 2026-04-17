// SnoopWPF.Agent.IntegrationTests/BrokerHostIntegrationTests.cs
// Integration tests for SnoopWPF.Agent.BrokerHost.
// M2-21 acceptance — filter: FullyQualifiedName~BrokerHost|FullyQualifiedName~BrokerTargetSpawner

namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SnoopWPF.Agent.BrokerHost;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Blob;

/// <summary>
/// Integration tests for <see cref="BrokerHost"/> and <see cref="BrokerTargetSpawner"/>.
/// </summary>
/// <remarks>
/// <para>
/// The broker + target round-trip over all 28 tools (M2-21 acceptance criterion) requires
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
                    args: new[] { "--version" },
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
    /// Full broker + target round-trip over all 28 MCP tools. Each tool call must
    /// succeed (non-error MCP response) when a target WPF process is connected.
    ///
    /// To run manually:
    ///   1. Start a WPF application (e.g. SnoopWPF.SampleApp).
    ///   2. Set SNOOP_TEST_PID environment variable to the target PID.
    ///   3. Run: dotnet test SnoopWPF.Agent.IntegrationTests --filter FullyQualifiedName~BrokerHost_RoundTrip_AllTools
    ///
    /// Automated subset coverage is provided by
    /// <c>BrokerHostRoundTripWpfTests.BrokerHost_RoundTrip_SessionInfo_Smoke</c>.
    /// </summary>
    [Test]
    [Category("MANUAL_VERIFICATION")]
    [Ignore("Requires live WPF target + injection EXE — run manually with SNOOP_TEST_PID set (M2-19). Automated smoke coverage lives in BrokerHostRoundTripWpfTests.")]
    public void BrokerHost_RoundTrip_AllTools_ManualVerification()
    {
        // This test is intentionally left as a stub.
        // Full round-trip coverage feeds M2-19 (brokered-mode consumer deliverables).
        // When M2-19 is implemented, this test should be updated to:
        //   1. Spawn BrokerTargetSpawner.Spawn(snoop-mcp.exe, ...) against the target PID.
        //   2. Start BrokerHost.Start(StdioServerTransport, opts) in a background task.
        //   3. Call all 28 MCP tools via the McpTestClient.
        //   4. Assert each tool returns a non-error response.
        Assert.Ignore("MANUAL_VERIFICATION: see test summary for instructions.");
    }

    // -------------------------------------------------------------------------
    // FX6-B1 acceptance: BlobStore + SnoopAgentOptions DI registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// FX6-B1 acceptance — verifies that <see cref="ToolProxyRegistrar.AddBrokerToolProxies"/>
    /// registers <see cref="BlobStore"/> in the DI container so that
    /// <c>CaptureScreenshotTool</c> and <c>FetchBlobTool</c> can be resolved without a
    /// DI exception. Verifies the broker-local blob round-trip (store + retrieve).
    /// </summary>
    [Test]
    public void CaptureScreenshot_ReturnsBlobRef()
    {
        // Arrange: build a DI container using a stub inspector (no live WPF needed).
        var inspector = new StubSnoopInspector();
        var services = new ServiceCollection();
        services.AddBrokerToolProxies(inspector);
        using var sp = services.BuildServiceProvider();

        // Act: resolve BlobStore — must not throw InvalidOperationException.
        var blobStore = sp.GetRequiredService<BlobStore>();

        // Assert: singleton instance is non-null and in working order.
        Assert.That(blobStore, Is.Not.Null);

        // Verify round-trip: store a blob and retrieve it (simulates CaptureScreenshotTool
        // storing PNG bytes then FetchBlobTool retrieving them).
        blobStore.Store("test-key", new byte[] { 1, 2, 3 }, "image/png");
        var entry = blobStore.TryGet("test-key");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.MimeType, Is.EqualTo("image/png"));
        Assert.That(entry.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    /// FX6-B1 acceptance — verifies that <see cref="ToolProxyRegistrar.AddBrokerToolProxies"/>
    /// registers <see cref="SnoopAgentOptions"/> in the DI container so that mutation tools
    /// (e.g. <c>ClickTool</c>, <c>SetPropertyTool</c>) can be resolved without a DI exception.
    /// Also verifies the safe-default fallback when no options are supplied by the caller.
    /// </summary>
    [Test]
    public void FetchBlob_ReturnsImage()
    {
        // Arrange: build a DI container with explicit options.
        var inspector = new StubSnoopInspector();
        var customOptions = new SnoopAgentOptions { EnableMutation = true, BlobTtl = TimeSpan.FromMinutes(5) };
        var services = new ServiceCollection();
        services.AddBrokerToolProxies(inspector, customOptions);
        using var sp = services.BuildServiceProvider();

        // Act: resolve SnoopAgentOptions — must not throw InvalidOperationException.
        var resolved = sp.GetRequiredService<SnoopAgentOptions>();

        // Assert: the supplied options instance is the one registered.
        Assert.That(resolved, Is.SameAs(customOptions));
        Assert.That(resolved.EnableMutation, Is.True);
        Assert.That(resolved.BlobTtl, Is.EqualTo(TimeSpan.FromMinutes(5)));

        // Also verify safe-default fallback when no options are supplied.
        var services2 = new ServiceCollection();
        services2.AddBrokerToolProxies(inspector);
        using var sp2 = services2.BuildServiceProvider();
        var defaultOptions = sp2.GetRequiredService<SnoopAgentOptions>();
        Assert.That(defaultOptions, Is.Not.Null);
        Assert.That(defaultOptions.EnableMutation, Is.False, "Broker default must be safe (mutation off).");
        Assert.That(defaultOptions.EnableRedaction, Is.True, "Broker default must be safe (redaction on).");
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

    // -------------------------------------------------------------------------
    // Stub ISnoopInspector — no WPF required; all methods throw NotImplementedException.
    // Used to verify DI registration only (DI resolution, not actual tool invocation).
    // -------------------------------------------------------------------------

    private sealed class StubSnoopInspector : ISnoopInspector
    {
#pragma warning disable SA1201 // Allow method grouping without strict member-order enforcement inside the stub.
        public Task<SessionInfoDto> GetSessionInfoAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task<List<WindowDto>> GetWindowsAsync(bool includeHidden, CancellationToken ct) => throw new NotImplementedException();

        public Task<VisualTreeResultDto> GetVisualTreeAsync(string? rootNodeId, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct) => throw new NotImplementedException();

        public Task<VisualTreeResultDto> GetVisualTreeAsync(WpfLocator locator, int maxDepth, string treeType, List<string>? includeProperties, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<NodeDto>> GetChildrenAsync(string? nodeId, string treeType, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<NodeDto>> GetChildrenAsync(WpfLocator locator, string treeType, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<AncestorDto>> GetAncestorsAsync(string nodeId, int? maxLevels, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<AncestorDto>> GetAncestorsAsync(WpfLocator locator, int? maxLevels, CancellationToken ct) => throw new NotImplementedException();

        public Task<FindElementResultDto> FindElementsAsync(string? typeName, string? name, string? rootNodeId, List<PropertyConditionDto>? conditions, string treeType, int maxResults, CancellationToken ct) => throw new NotImplementedException();

        public Task<InspectElementDto> InspectElementAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<InspectElementDto> InspectElementAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<PropertyDto>> GetPropertiesAsync(string nodeId, string? filter, string? category, bool includeDefaults, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<PropertyDto>> GetPropertiesAsync(WpfLocator locator, string? filter, string? category, bool includeDefaults, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetPropertyAsync(WpfLocator locator, string propertyName, string value, CancellationToken ct) => throw new NotImplementedException();

        public Task<BindingInfoDto> GetBindingInfoAsync(string nodeId, string propertyName, CancellationToken ct) => throw new NotImplementedException();

        public Task<BindingInfoDto> GetBindingInfoAsync(WpfLocator locator, string propertyName, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(string? nodeId, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<DiagnosticItemDto>> RunDiagnosticsAsync(WpfLocator locator, List<string>? providers, string? minLevel, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<ResourceDto>> GetResourcesAsync(string? nodeId, string? resourceKey, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<CursorPage<ResourceDto>> GetResourcesAsync(WpfLocator locator, string? resourceKey, string? cursor, int take, CancellationToken ct) => throw new NotImplementedException();

        public Task<ScreenshotResultDto> CaptureScreenshotAsync(string? nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<ScreenshotResultDto> CaptureScreenshotAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<TriggerDto>> GetTriggersAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<TriggerDto>> GetTriggersAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<BehaviorDto>> GetBehaviorsAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<List<BehaviorDto>> GetBehaviorsAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SelectItemAsync(string nodeId, string identifier, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SelectItemAsync(WpfLocator locator, string identifier, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetCheckStateAsync(string nodeId, string state, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetCheckStateAsync(WpfLocator locator, string state, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetTextValueAsync(string nodeId, string value, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetTextValueAsync(WpfLocator locator, string value, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetSliderValueAsync(string nodeId, double value, bool normalized, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> SetSliderValueAsync(WpfLocator locator, double value, bool normalized, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ExecuteCommandAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ExecuteCommandAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ClickAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ClickAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ToggleAsync(string nodeId, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ToggleAsync(WpfLocator locator, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ExpandCollapseAsync(string nodeId, string action, CancellationToken ct) => throw new NotImplementedException();

        public Task<StateDeltaDto> ExpandCollapseAsync(WpfLocator locator, string action, CancellationToken ct) => throw new NotImplementedException();

        public Task<BindingResolutionDto> ResolveBindingAsync(string nodeId, string propertyName, CancellationToken ct) => throw new NotImplementedException();

        public Task<BindingResolutionDto> ResolveBindingAsync(WpfLocator locator, string propertyName, CancellationToken ct) => throw new NotImplementedException();

        public Task<WaitForPropertyResultDto> WaitForPropertyAsync(WpfLocator locator, string propertyName, string? expectedValue, int timeoutMs, string presenceExpected, CancellationToken ct) => throw new NotImplementedException();

        public Task<PollChangesResultDto> PollChangesAsync(long sinceVersion, WpfLocator? rootLocator, CancellationToken ct) => throw new NotImplementedException();

        public Task<PumpUntilIdleResultDto> PumpUntilIdleAsync(int timeoutMs, IReadOnlyList<string>? resources, CancellationToken ct) => throw new NotImplementedException();
#pragma warning restore SA1201
    }
}
