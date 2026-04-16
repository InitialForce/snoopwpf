namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Basic end-to-end smoke tests that verify the integration test harness works
/// and the SnoopInspector can connect to a real WPF application.
/// </summary>
[TestFixture]
public sealed class SmokeTests : WpfIntegrationTestBase
{
    /// <summary>
    /// Verifies that the TestWpfApp starts successfully and that a McpTestClient
    /// can be created against it.
    /// </summary>
    [Test]
    public void TestHarness_CreatesWithoutError()
    {
        Assert.That(this.WpfApp, Is.Not.Null, "WpfApp should be created.");
        Assert.That(this.Client, Is.Not.Null, "McpTestClient should be created.");
        Assert.That(this.WpfApp.Dispatcher, Is.Not.Null, "WPF Dispatcher should be available.");
    }

    /// <summary>
    /// Verifies that GetSessionInfo returns a valid response with process-level data.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_ReturnsValidResponse()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        Assert.That(info, Is.Not.Null);
        Assert.That(info.Pid, Is.GreaterThan(0), "PID must be a positive integer.");
        Assert.That(info.ProcessName, Is.Not.Null.And.Not.Empty, "Process name must be populated.");
        Assert.That(info.DotnetVersion, Is.Not.Null.And.Not.Empty, "DotnetVersion must be populated.");
    }

    /// <summary>
    /// Verifies that GetWindows returns at least the main test window.
    /// </summary>
    [Test]
    public async Task GetWindows_ReturnsAtLeastOneWindow()
    {
        var windows = await this.Client.GetWindowsAsync(includeHidden: false).ConfigureAwait(false);

        Assert.That(windows, Is.Not.Null);
        Assert.That(windows.Count, Is.GreaterThan(0), "At least one window must be returned.");
    }

    /// <summary>
    /// Verifies that calling GetWindows twice returns consistent results (same count).
    /// </summary>
    [Test]
    public async Task GetWindows_CalledTwice_ReturnsConsistentCount()
    {
        var windows1 = await this.Client.GetWindowsAsync(includeHidden: false).ConfigureAwait(false);
        var windows2 = await this.Client.GetWindowsAsync(includeHidden: false).ConfigureAwait(false);

        Assert.That(windows1.Count, Is.EqualTo(windows2.Count),
            "Window count must be stable across consecutive calls.");
    }

    /// <summary>
    /// Verifies that GetChildren at root level returns children.
    /// </summary>
    [Test]
    public async Task GetChildren_AtRoot_ReturnsNodes()
    {
        var page = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);

        Assert.That(page, Is.Not.Null);
        Assert.That(page.Items, Is.Not.Null);
        Assert.That(page.Items.Count, Is.GreaterThan(0), "Root-level should have at least one child.");
    }

    /// <summary>
    /// Verifies that node IDs are non-empty strings.
    /// </summary>
    [Test]
    public async Task GetChildren_NodeIds_AreNonEmpty()
    {
        var page = await this.Client.GetChildrenAsync(nodeId: null).ConfigureAwait(false);

        foreach (var node in page.Items)
        {
            Assert.That(node.NodeId, Is.Not.Null.And.Not.Empty,
                $"Node '{node.DisplayName}' must have a non-empty NodeId.");
        }
    }
}
