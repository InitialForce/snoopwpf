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

    // -----------------------------------------------------------------------
    // Pagination edge cases
    // -----------------------------------------------------------------------

    [Test]
    public void GetPage_ZeroItemSnapshot_TakeEqualsPageSize_ReturnsEmptyNoMore()
    {
        // Regression: take=50 against a zero-item snapshot must not return hasMore or a cursor.
        var token = this.manager.CreateCursor(Array.Empty<string>());

        var page = this.manager.GetPage(token, 50);

        Assert.That(page.Items, Is.Empty, "items must be empty for 0-item snapshot");
        Assert.That(page.TotalCount, Is.EqualTo(0), "totalCount must be 0");
        Assert.That(page.HasMore, Is.False, "hasMore must be false for 0-item snapshot");
        Assert.That(page.NextCursor, Is.Null, "no cursor should be returned when there are no items");
    }

    [Test]
    public void GetPage_SnapshotExactlyFiftyItems_TakeFifty_HasMoreIsFalseNoCursor()
    {
        // Regression: when snapshot size == take the last page is fully consumed;
        // hasMore must be false and no stale cursor should be left behind.
        var ids = Enumerable.Range(1, 50).Select(i => $"node:{i}").ToArray();
        var token = this.manager.CreateCursor(ids);

        var page = this.manager.GetPage(token, 50);

        Assert.That(page.Items.Count, Is.EqualTo(50), "all 50 items must be returned");
        Assert.That(page.TotalCount, Is.EqualTo(50));
        Assert.That(page.HasMore, Is.False, "hasMore must be false when snapshot fits exactly in one page");
        Assert.That(page.NextCursor, Is.Null, "cursor must be null — no further pages");

        // After consuming the cursor the entry should be removed; a subsequent call with
        // the same token should behave as if the cursor was never created (empty page).
        var stalePage = this.manager.GetPage(token, 50);
        Assert.That(stalePage.Items, Is.Empty, "consumed cursor must not return items on re-use");
    }

    [Test]
    public void GetPage_TakeOne_SingleItem_ReturnsItemAndNoMore()
    {
        var token = this.manager.CreateCursor(new[] { "only-item" });

        var page = this.manager.GetPage(token, 1);

        Assert.That(page.Items, Is.EqualTo(new[] { "only-item" }));
        Assert.That(page.TotalCount, Is.EqualTo(1));
        Assert.That(page.HasMore, Is.False);
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public void GetPage_FiftyOnePlusOneRemainder_FirstPageHasMoreSecondPageDoesNot()
    {
        // take=50, snapshot=51 → first page: 50 items, hasMore=true, cursor returned;
        // second page: 1 item, hasMore=false, no cursor.
        var ids = Enumerable.Range(1, 51).Select(i => $"n:{i}").ToArray();
        var token = this.manager.CreateCursor(ids);

        var firstPage = this.manager.GetPage(token, 50);

        Assert.That(firstPage.Items.Count, Is.EqualTo(50), "first page must contain 50 items");
        Assert.That(firstPage.TotalCount, Is.EqualTo(51));
        Assert.That(firstPage.HasMore, Is.True, "hasMore must be true — one item remains");
        Assert.That(firstPage.NextCursor, Is.Not.Null, "cursor must be non-null when there are more pages");

        var secondPage = this.manager.GetPage(firstPage.NextCursor, 50);

        Assert.That(secondPage.Items.Count, Is.EqualTo(1), "second page must contain the single remaining item");
        Assert.That(secondPage.HasMore, Is.False, "hasMore must be false on the final page");
        Assert.That(secondPage.NextCursor, Is.Null, "no cursor after last page");
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

    // -----------------------------------------------------------------------
    // FX6-A2: node-binding / CursorMismatch rejection
    // -----------------------------------------------------------------------

    [Test]
    public void RejectsMismatchedNodeId()
    {
        // Arrange: manager with signing key, cursor bound to node "0:1".
        var signingKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(signingKey);
        var boundManager = new CursorManager(
            clock: () => this.now,
            ttl: TimeSpan.FromSeconds(30),
            sweepInterval: TimeSpan.FromHours(1),
            signingKey: signingKey);

        var token = boundManager.CreateCursor(new[] { "a", "b", "c" }, nodeId: "0:1");

        // Act / Assert: replaying cursor against nodeId "0:2" must throw CursorMismatch.
        var ex = Assert.Throws<SnoopWPF.Agent.Contracts.SnoopException>(
            () => boundManager.GetPage(token, 10, nodeId: "0:2"));

        Assert.That(ex!.Code, Is.EqualTo(SnoopWPF.Agent.Contracts.SnoopErrorCode.CursorMismatch));

        boundManager.Dispose();
    }

    [Test]
    public void AcceptsMatchingNodeId()
    {
        // Arrange: manager with signing key, cursor bound to node "0:1".
        var signingKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(signingKey);
        var boundManager = new CursorManager(
            clock: () => this.now,
            ttl: TimeSpan.FromSeconds(30),
            sweepInterval: TimeSpan.FromHours(1),
            signingKey: signingKey);

        var token = boundManager.CreateCursor(new[] { "a", "b", "c" }, nodeId: "0:1");

        // Should NOT throw when the correct nodeId is supplied.
        var page = boundManager.GetPage(token, 10, nodeId: "0:1");
        Assert.That(page.Items, Is.Not.Empty);

        boundManager.Dispose();
    }

    [Test]
    public void UnboundCursor_AcceptsAnyNodeId()
    {
        // Unbound cursors (no signingKey / no nodeId on create) must be accepted regardless
        // of the nodeId supplied to GetPage — backwards compatibility.
        var token = this.manager.CreateCursor(new[] { "a", "b" });

        // Should not throw even when passing a nodeId.
        var page = this.manager.GetPage(token, 10, nodeId: "some-node");
        Assert.That(page.Items.Count, Is.EqualTo(2));
    }

    [Test]
    public void BoundCursor_ParseIsO1()
    {
        // Verify that cursor lookup is O(1) dictionary lookup + constant-time HMAC verify
        // (no iteration of snapshot) — just check it returns in well under 1 ms for a 10k-item snapshot.
        var signingKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(signingKey);
        var boundManager = new CursorManager(
            clock: () => this.now,
            ttl: TimeSpan.FromSeconds(30),
            sweepInterval: TimeSpan.FromHours(1),
            signingKey: signingKey);

        var largeSnapshot = Enumerable.Range(1, 10_000).Select(i => $"node:{i}").ToArray();
        var token = boundManager.CreateCursor(largeSnapshot, nodeId: "0:big");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var page = boundManager.GetPage(token, 50, nodeId: "0:big");
        sw.Stop();

        Assert.That(page.Items.Count, Is.EqualTo(50));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "GetPage must complete well within 100 ms for O(1) verification");

        boundManager.Dispose();
    }
}
