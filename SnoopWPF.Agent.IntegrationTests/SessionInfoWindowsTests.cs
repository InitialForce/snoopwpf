namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Verifies that wpf_get_session_info inlines top-level windows (M1-08).
/// </summary>
[TestFixture]
public sealed class SessionInfoWindowsTests : WpfIntegrationTestBase
{
    /// <summary>
    /// GetSessionInfo must return a non-empty windows array when a WPF window is visible.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_Windows_IsNonEmpty()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        Assert.That(info.Windows, Is.Not.Null, "Windows list must not be null.");
        Assert.That(info.Windows.Count, Is.GreaterThan(0), "At least one window must be inlined.");
    }

    /// <summary>
    /// Each inlined window must have a valid node ID.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_Windows_HaveNonEmptyNodeIds()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        foreach (var w in info.Windows)
        {
            Assert.That(w.NodeId, Is.Not.Null.And.Not.Empty,
                "Every window summary must have a non-empty NodeId.");
        }
    }

    /// <summary>
    /// Each inlined window must report positive dimensions.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_Windows_HavePositiveDimensions()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        foreach (var w in info.Windows)
        {
            Assert.That(w.Width, Is.GreaterThan(0), $"Window '{w.Title}' must have positive Width.");
            Assert.That(w.Height, Is.GreaterThan(0), $"Window '{w.Title}' must have positive Height.");
        }
    }

    /// <summary>
    /// Each inlined window must carry a non-empty locator string.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_Windows_HaveLocators()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        foreach (var w in info.Windows)
        {
            Assert.That(w.Locator, Is.Not.Null.And.Not.Empty,
                $"Window '{w.Title}' must have a non-empty Locator.");
        }
    }

    /// <summary>
    /// The node IDs in the inlined windows must be a subset of the IDs reported
    /// by the dispatcher's window node ID list.
    /// </summary>
    [Test]
    public async Task GetSessionInfo_Windows_NodeIdsConsistentWithDispatchers()
    {
        var info = await this.Client.GetSessionInfoAsync().ConfigureAwait(false);

        var dispatcherNodeIds = info.Dispatchers
            .SelectMany(d => d.WindowNodeIds)
            .ToHashSet();

        foreach (var w in info.Windows)
        {
            Assert.That(dispatcherNodeIds, Does.Contain(w.NodeId),
                $"Window NodeId '{w.NodeId}' must appear in a dispatcher's WindowNodeIds.");
        }
    }
}
