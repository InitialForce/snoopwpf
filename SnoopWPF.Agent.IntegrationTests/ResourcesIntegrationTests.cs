namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Integration tests for resource dictionary inspection against a real WPF application.
/// Exercises <c>GetResourcesAsync</c> through the <see cref="McpTestClient"/>.
/// </summary>
[TestFixture]
public sealed class ResourcesIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // GetResourcesAsync — basic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that GetResourcesAsync returns a non-null page.
    /// </summary>
    [Test]
    public async Task GetResources_ReturnsPage()
    {
        var page = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: null,
                cursor: null,
                take: 100,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(page, Is.Not.Null, "GetResourcesAsync must return a non-null page.");
        Assert.That(page.Items, Is.Not.Null, "Items collection must not be null.");
        Assert.That(page.TotalCount, Is.GreaterThanOrEqualTo(0),
            "TotalCount must be non-negative.");
    }

    /// <summary>
    /// Verifies that every returned resource has a non-empty Key and ValueTypeName.
    /// </summary>
    [Test]
    public async Task GetResources_AllItems_HaveKeyAndType()
    {
        var page = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: null,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        foreach (var resource in page.Items)
        {
            Assert.That(resource.Key, Is.Not.Null,
                "Resource Key must not be null.");
            Assert.That(resource.ValueTypeName, Is.Not.Null,
                $"Resource '{resource.Key}' must have a non-null ValueTypeName.");
        }
    }

    /// <summary>
    /// Verifies that every returned resource has a non-null Origin field.
    /// </summary>
    [Test]
    public async Task GetResources_AllItems_HaveOrigin()
    {
        var page = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: null,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        foreach (var resource in page.Items)
        {
            Assert.That(resource.Origin, Is.Not.Null,
                $"Resource '{resource.Key}' must have a non-null Origin.");
        }
    }

    /// <summary>
    /// Verifies that resource key filtering narrows results when a matching key is provided.
    /// </summary>
    [Test]
    public async Task GetResources_WithKeyFilter_NarrowsResults()
    {
        // Get all resources first to find a key we can filter on.
        var allPage = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: null,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        if (allPage.Items.Count == 0)
        {
            Assert.Ignore("No resources found in application; test inconclusive.");
            return;
        }

        // Pick a key that exists.
        var existingKey = allPage.Items[0].Key;
        if (string.IsNullOrEmpty(existingKey))
        {
            Assert.Ignore("First resource has empty key; test inconclusive.");
            return;
        }

        var filteredPage = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: existingKey,
                cursor: null,
                take: 200,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(filteredPage.Items.Count, Is.LessThanOrEqualTo(allPage.Items.Count),
            "Filtered results must not exceed total results.");
        Assert.That(filteredPage.Items, Is.Not.Empty,
            $"Filter by key '{existingKey}' should return at least one result.");
    }

    /// <summary>
    /// Verifies that filtering by a non-existent key returns no results.
    /// </summary>
    [Test]
    public async Task GetResources_NonExistentKey_ReturnsEmpty()
    {
        var page = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: null,
                resourceKey: "ThisKeyDefinitelyDoesNotExist_xyz_12345",
                cursor: null,
                take: 100,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(page.Items, Is.Empty,
            "Filtering by a non-existent key should return zero resources.");
    }

    /// <summary>
    /// Verifies that GetResourcesAsync on a specific window node does not throw.
    /// </summary>
    [Test]
    public async Task GetResources_OnWindowNode_DoesNotThrow()
    {
        var windows = await this.Client.GetWindowsAsync(includeHidden: false).ConfigureAwait(false);

        if (windows.Count == 0)
        {
            Assert.Ignore("No visible windows found; test inconclusive.");
            return;
        }

        var windowNodeId = windows[0].NodeId;

        var page = await this.Client.Inspector
            .GetResourcesAsync(
                nodeId: windowNodeId,
                resourceKey: null,
                cursor: null,
                take: 100,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(page, Is.Not.Null);
        Assert.That(page.Items, Is.Not.Null);
    }
}
