namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Infrastructure;

[TestFixture]
public class CursorManagerTests : IDisposable
{
    private DateTimeOffset now;

    private CursorManager manager = null!;

    [SetUp]
    public void SetUp()
    {
        this.now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        this.manager = new CursorManager(
            clock: () => this.now,
            ttl: TimeSpan.FromSeconds(30),
            sweepInterval: TimeSpan.FromHours(1));
    }

    [TearDown]
    public void TearDown()
    {
        this.manager?.Dispose();
    }

    public void Dispose()
    {
        this.manager?.Dispose();
    }

    [Test]
    public void CreateCursor_ReturnsOpaqueToken()
    {
        var token = this.manager.CreateCursor(new[] { "0:1", "0:2", "0:3" });

        Assert.That(token, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetPage_NoCursor_ReturnsEmptyPage()
    {
        var page = this.manager.GetPage(null, 10);

        Assert.That(page.Items, Is.Empty);
        Assert.That(page.TotalCount, Is.EqualTo(0));
        Assert.That(page.HasMore, Is.False);
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public void GetPage_FirstPage_ReturnsCorrectItems()
    {
        var ids = new[] { "0:1", "0:2", "0:3", "0:4", "0:5" };
        var token = this.manager.CreateCursor(ids);

        var page = this.manager.GetPage(token, 3);

        Assert.That(page.Items, Is.EqualTo(new[] { "0:1", "0:2", "0:3" }));
        Assert.That(page.TotalCount, Is.EqualTo(5));
        Assert.That(page.HasMore, Is.True);
        Assert.That(page.NextCursor, Is.EqualTo(token));
    }

    [Test]
    public void GetPage_SecondPage_ReturnsNextItems()
    {
        var ids = new[] { "0:1", "0:2", "0:3", "0:4", "0:5" };
        var token = this.manager.CreateCursor(ids);

        this.manager.GetPage(token, 3);
        var page2 = this.manager.GetPage(token, 3);

        Assert.That(page2.Items, Is.EqualTo(new[] { "0:4", "0:5" }));
        Assert.That(page2.HasMore, Is.False);
        Assert.That(page2.NextCursor, Is.Null);
    }

    [Test]
    public void GetPage_ExactPageSize_ConsumesAll()
    {
        var ids = new[] { "0:1", "0:2", "0:3" };
        var token = this.manager.CreateCursor(ids);

        var page = this.manager.GetPage(token, 3);

        Assert.That(page.Items.Count, Is.EqualTo(3));
        Assert.That(page.HasMore, Is.False);
    }

    [Test]
    public void GetPage_AfterConsumed_ReturnsEmpty()
    {
        var ids = new[] { "0:1", "0:2" };
        var token = this.manager.CreateCursor(ids);

        this.manager.GetPage(token, 10);
        var page = this.manager.GetPage(token, 10);

        Assert.That(page.Items, Is.Empty);
        Assert.That(page.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public void GetPage_WithinTtl_NotStale()
    {
        var token = this.manager.CreateCursor(new[] { "0:1", "0:2" });

        this.now = this.now.AddSeconds(29);
        var page = this.manager.GetPage(token, 10);

        Assert.That(page.Stale, Is.False);
    }

    [Test]
    public void GetPage_AfterTtlExpiry_IsStale()
    {
        var token = this.manager.CreateCursor(new[] { "0:1", "0:2" });

        this.now = this.now.AddSeconds(31);
        var page = this.manager.GetPage(token, 10);

        Assert.That(page.Stale, Is.True);
        Assert.That(page.Items.Count, Is.GreaterThan(0));
    }

    [Test]
    public void GetPage_EmptySnapshot_ReturnsEmptyPage()
    {
        var token = this.manager.CreateCursor(Array.Empty<string>());

        var page = this.manager.GetPage(token, 10);

        Assert.That(page.Items, Is.Empty);
        Assert.That(page.TotalCount, Is.EqualTo(0));
        Assert.That(page.HasMore, Is.False);
    }

    [Test]
    public void Clear_RemovesAllSnapshots()
    {
        var token = this.manager.CreateCursor(new[] { "0:1", "0:2", "0:3" });

        this.manager.Clear();

        var page = this.manager.GetPage(token, 10);
        Assert.That(page.Items, Is.Empty);
    }

    [Test]
    public void MultipleCursors_AreIndependent()
    {
        var token1 = this.manager.CreateCursor(new[] { "a", "b", "c" });
        var token2 = this.manager.CreateCursor(new[] { "x", "y", "z" });

        var page1 = this.manager.GetPage(token1, 2);
        var page2 = this.manager.GetPage(token2, 2);

        Assert.That(page1.Items, Does.Contain("a"));
        Assert.That(page2.Items, Does.Contain("x"));
        Assert.That(page1.Items, Does.Not.Contain("x"));
    }

    [Test]
    public void CreateCursor_GeneratesUniqueTokens()
    {
        var token1 = this.manager.CreateCursor(new[] { "a" });
        var token2 = this.manager.CreateCursor(new[] { "b" });

        Assert.That(token1, Is.Not.EqualTo(token2));
    }

    [Test]
    public void Dispose_PreventsSubsequentUse()
    {
        this.manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => this.manager.CreateCursor(new[] { "a" }));
    }

    [Test]
    public void GetPage_ConcurrentCallers_EachItemReturnedExactlyOnce()
    {
        // Arrange: 20-item snapshot, page size 1 so each call advances by one slot.
        const int itemCount = 20;
        const int pageSize = 1;
        const int taskCount = 4;

        var snapshot = Enumerable.Range(1, itemCount).Select(i => $"item:{i}").ToArray();
        var token = this.manager.CreateCursor(snapshot);

        var collected = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act: 4 tasks race to drain the cursor.
        var tasks = Enumerable.Range(0, taskCount).Select(_ => Task.Run(() =>
        {
            // Each task keeps calling GetPage until there is no more data for this token.
            while (true)
            {
                var page = this.manager.GetPage(token, pageSize);
                foreach (var item in page.Items)
                {
                    collected.Add(item);
                }

                // Stop when the token is exhausted (hasMore == false and items empty means consumed).
                if (!page.HasMore)
                {
                    break;
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        // Assert: union of all collected items equals the snapshot exactly — no duplicates, no missing.
        var sorted = collected.OrderBy(x => x).ToArray();
        var expected = snapshot.OrderBy(x => x).ToArray();

        Assert.That(sorted.Length, Is.EqualTo(itemCount), "Total items collected must equal snapshot size (no duplicates, no missing items).");
        Assert.That(sorted, Is.EqualTo(expected), "Collected items must match snapshot exactly.");
    }
}
