namespace SnoopWPF.Agent.Tests.Diagnostics;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Diagnostics;

/// <summary>
/// Unit tests for <see cref="SnoopAgentContext"/> — AsyncLocal scope correctness.
/// </summary>
[TestFixture]
public class SnoopAgentContextTests
{
    // ── Scope lifecycle ─────────────────────────────────────────────────────────

    [Test]
    public void BeginScope_ThenAddWarning_DrainReturnsWarning()
    {
        using var scope = SnoopAgentContext.BeginScope();

        SnoopAgentContext.AddWarning("CODE_A", "message a");

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings.Count, Is.EqualTo(1));
        Assert.That(warnings[0].Code, Is.EqualTo("CODE_A"));
        Assert.That(warnings[0].Message, Is.EqualTo("message a"));
    }

    [Test]
    public void BeginScope_NoWarningsAdded_DrainReturnsEmpty()
    {
        using var scope = SnoopAgentContext.BeginScope();

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void AddWarning_OutsideScope_IsNoOp()
    {
        // Ensure no scope is active by disposing any existing one first.
        // We read DrainWarnings to clear whatever previous tests might have left.
        SnoopAgentContext.DrainWarnings();

        // Explicitly do NOT call BeginScope().
        // AddWarning outside scope must not throw and must not accumulate anything.
        Assert.DoesNotThrow(() => SnoopAgentContext.AddWarning("X", "should be silently dropped"));
    }

    [Test]
    public void MultipleWarnings_ReturnInInsertionOrder()
    {
        using var scope = SnoopAgentContext.BeginScope();

        SnoopAgentContext.AddWarning("FIRST", "first warning");
        SnoopAgentContext.AddWarning("SECOND", "second warning");
        SnoopAgentContext.AddWarning("THIRD", "third warning");

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings.Count, Is.EqualTo(3));
        Assert.That(warnings[0].Code, Is.EqualTo("FIRST"));
        Assert.That(warnings[1].Code, Is.EqualTo("SECOND"));
        Assert.That(warnings[2].Code, Is.EqualTo("THIRD"));
    }

    [Test]
    public void DrainWarnings_ClearsAccumulatedList()
    {
        using var scope = SnoopAgentContext.BeginScope();

        SnoopAgentContext.AddWarning("CODE", "msg");

        var first = SnoopAgentContext.DrainWarnings();
        var second = SnoopAgentContext.DrainWarnings();

        Assert.That(first.Count, Is.EqualTo(1));
        Assert.That(second, Is.Empty, "Second drain after first must return empty (double-drain guard).");
    }

    [Test]
    public void Dispose_ClearsScope_SubsequentAddIsNoOp()
    {
        var scope = SnoopAgentContext.BeginScope();
        scope.Dispose();

        // After disposal the scope is gone — AddWarning should be a no-op.
        SnoopAgentContext.AddWarning("POST_DISPOSE", "should not accumulate");

        var warnings = SnoopAgentContext.DrainWarnings();
        Assert.That(warnings, Is.Empty);
    }

    // ── Warning record shape ────────────────────────────────────────────────────

    [Test]
    public void Warning_RecordsTimestamp()
    {
        using var scope = SnoopAgentContext.BeginScope();

        var before = System.DateTimeOffset.UtcNow;
        SnoopAgentContext.AddWarning("TS", "timestamp test");
        var after = System.DateTimeOffset.UtcNow;

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings[0].Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(warnings[0].Timestamp, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void Warning_SupportsNullData()
    {
        using var scope = SnoopAgentContext.BeginScope();

        SnoopAgentContext.AddWarning("NO_DATA", "no extra data", data: null);

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings[0].Data, Is.Null);
    }

    [Test]
    public void Warning_SupportsStructuredData()
    {
        using var scope = SnoopAgentContext.BeginScope();
        var payload = new { Key = "value", Count = 42 };

        SnoopAgentContext.AddWarning("WITH_DATA", "has data", data: payload);

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings[0].Data, Is.SameAs(payload));
    }

    // ── AsyncLocal scope isolation across await boundaries ───────────────────────

    [Test]
    public async Task AsyncLocal_ScopeIsolated_AcrossAwaitBoundaries()
    {
        // Verify warnings survive await points within the same scope.
        using var scope = SnoopAgentContext.BeginScope();

        SnoopAgentContext.AddWarning("PRE_AWAIT", "before await");

        await Task.Yield();

        SnoopAgentContext.AddWarning("POST_AWAIT", "after await");

        var warnings = SnoopAgentContext.DrainWarnings();

        Assert.That(warnings.Count, Is.EqualTo(2));
        Assert.That(warnings[0].Code, Is.EqualTo("PRE_AWAIT"));
        Assert.That(warnings[1].Code, Is.EqualTo("POST_AWAIT"));
    }

    [Test]
    public async Task AsyncLocal_ConcurrentScopes_DoNotInterfer()
    {
        // Two concurrent async tasks each begin their own scope.
        // Each should see only its own warnings.
        var task1Results = new List<AgentWarning>();
        var task2Results = new List<AgentWarning>();

        var barrier = new System.Threading.SemaphoreSlim(0, 1);

        var t1 = Task.Run(async () =>
        {
            using var scope = SnoopAgentContext.BeginScope();
            SnoopAgentContext.AddWarning("T1_A", "task1 first");

            // Let t2 start and add its own warning before we continue.
            barrier.Release();
            await Task.Delay(20);

            SnoopAgentContext.AddWarning("T1_B", "task1 second");
            task1Results.AddRange(SnoopAgentContext.DrainWarnings());
        });

        var t2 = Task.Run(async () =>
        {
            // Wait until t1 has added its first warning.
            await barrier.WaitAsync();

            using var scope = SnoopAgentContext.BeginScope();
            SnoopAgentContext.AddWarning("T2_ONLY", "task2 only");

            await Task.Yield();

            task2Results.AddRange(SnoopAgentContext.DrainWarnings());
        });

        await Task.WhenAll(t1, t2);

        // t1 should have exactly T1_A and T1_B (not T2_ONLY).
        Assert.That(task1Results.Select(w => w.Code), Is.EquivalentTo(new[] { "T1_A", "T1_B" }));

        // t2 should have exactly T2_ONLY (not T1_A or T1_B).
        Assert.That(task2Results.Select(w => w.Code), Is.EquivalentTo(new[] { "T2_ONLY" }));
    }

    // ── Nested scopes ────────────────────────────────────────────────────────────

    [Test]
    public void NestedScope_InnerScopeReplacesOuter()
    {
        // Nested BeginScope() replaces the current scope; outer warnings are lost.
        // This is the documented behaviour: each tool invocation creates a fresh scope.
        using var outer = SnoopAgentContext.BeginScope();
        SnoopAgentContext.AddWarning("OUTER", "outer warning");

        using var inner = SnoopAgentContext.BeginScope();
        SnoopAgentContext.AddWarning("INNER", "inner warning");

        var innerWarnings = SnoopAgentContext.DrainWarnings();
        Assert.That(innerWarnings.Count, Is.EqualTo(1));
        Assert.That(innerWarnings[0].Code, Is.EqualTo("INNER"));
    }
}
