namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Infrastructure;

[TestFixture]
public class NodeRegistryTests : IDisposable
{
    private NodeRegistry registry = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a very long sweep interval so tests control sweep explicitly
        this.registry = new NodeRegistry(sweepInterval: TimeSpan.FromHours(1));
    }

    [TearDown]
    public void TearDown()
    {
        this.registry?.Dispose();
    }

    public void Dispose()
    {
        this.registry?.Dispose();
    }

    [Test]
    public void GetOrCreateId_SameObject_ReturnsSameId()
    {
        var obj = new object();
        var id1 = this.registry.GetOrCreateId(obj);
        var id2 = this.registry.GetOrCreateId(obj);

        Assert.That(id1, Is.EqualTo(id2));
    }

    [Test]
    public void GetOrCreateId_DifferentObjects_ReturnsDifferentIds()
    {
        var obj1 = new object();
        var obj2 = new object();

        var id1 = this.registry.GetOrCreateId(obj1);
        var id2 = this.registry.GetOrCreateId(obj2);

        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    [Test]
    public void GetOrCreateId_IdFormat_StartsWithZeroColon()
    {
        var obj = new object();
        var id = this.registry.GetOrCreateId(obj);

        Assert.That(id, Does.StartWith("0:"));
    }

    [Test]
    public void TryResolve_KnownId_ReturnsObject()
    {
        var obj = new object();
        var id = this.registry.GetOrCreateId(obj);

        var resolved = this.registry.TryResolve(id);

        Assert.That(resolved, Is.SameAs(obj));
    }

    [Test]
    public void TryResolve_UnknownId_ReturnsNull()
    {
        var result = this.registry.TryResolve("0:99999");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryResolve_AfterClear_ReturnsNull()
    {
        var obj = new object();
        var id = this.registry.GetOrCreateId(obj);

        this.registry.Clear();

        var resolved = this.registry.TryResolve(id);
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void Clear_ResetsIdGeneration()
    {
        var obj1 = new object();
        this.registry.GetOrCreateId(obj1);
        this.registry.Clear();

        var obj2 = new object();
        var id2 = this.registry.GetOrCreateId(obj2);

        Assert.That(id2, Does.StartWith("0:"));
    }

    [Test]
    public void ForceSweep_PrunesDeadEntries()
    {
        string? id = null;
        this.CreateAndForget(ref id);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        this.registry.ForceSweep();

        var resolved = this.registry.TryResolve(id!);
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void TryResolve_AfterObjectCollected_LazilyPrunesEntry()
    {
        string? id = null;
        this.CreateAndForget(ref id);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var result = this.registry.TryResolve(id!);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetOrCreateId_IsIdempotentUnderConcurrency()
    {
        var obj = new object();
        var barrier = new Barrier(8);

        var threads = new Thread[8];
        var ids = new string[8];

        for (var i = 0; i < threads.Length; i++)
        {
            var idx = i;
            threads[idx] = new Thread(() =>
            {
                barrier.SignalAndWait();
                ids[idx] = this.registry.GetOrCreateId(obj);
            });
        }

        foreach (var t in threads)
        {
            t.Start();
        }

        foreach (var t in threads)
        {
            t.Join();
        }

        Assert.That(ids, Is.All.EqualTo(ids[0]));
    }

    [Test]
    public void Dispose_PreventsSubsequentUse()
    {
        this.registry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => this.registry.GetOrCreateId(new object()));
    }

    private void CreateAndForget(ref string? id)
    {
        var obj = new object();
        id = this.registry.GetOrCreateId(obj);
    }
}
