namespace SnoopWPF.Agent.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine.Sync;

/// <summary>
/// Tests for <see cref="IIdlingResource"/> implementations and the
/// <see cref="IdlingResourceRegistry"/> AND-gate.
///
/// All tests that exercise Dispatcher-backed resources run on an STA thread
/// with a real <see cref="Dispatcher"/>.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class IdlingResourceTests
{
    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    private static void DrainContextIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.InvokeAsync(
            () => { frame.Continue = false; },
            DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    // ------------------------------------------------------------------ //
    //  DispatcherIdlingResource
    // ------------------------------------------------------------------ //

    /// <summary>Resource reports idle once a ContextIdle probe completes.</summary>
    [Test]
    public void DispatcherIdlingResource_BecomesIdle_AfterContextIdle()
    {
        using var resource = new DispatcherIdlingResource(Dispatcher.CurrentDispatcher);

        DrainContextIdle();

        Assert.That(resource.IsIdle, Is.True,
            "Resource should report idle once a ContextIdle probe completes.");
    }

    /// <summary>IdleChanged is raised after the ContextIdle probe.</summary>
    [Test]
    public void DispatcherIdlingResource_RaisesIdleChangedEvent()
    {
        using var resource = new DispatcherIdlingResource(Dispatcher.CurrentDispatcher);

        var events = new List<bool>();
        resource.IdleChanged += (_, e) => events.Add(e.IsIdle);

        DrainContextIdle();

        Assert.That(events, Has.Count.GreaterThanOrEqualTo(1),
            "IdleChanged should have been raised at least once.");
        Assert.That(events[events.Count - 1], Is.True, "Last event should indicate idle.");
    }

    /// <summary>CheckPriority is ContextIdle.</summary>
    [Test]
    public void DispatcherIdlingResource_HasCorrectCheckPriority()
    {
        using var resource = new DispatcherIdlingResource(Dispatcher.CurrentDispatcher);
        Assert.That(resource.CheckPriority, Is.EqualTo(DispatcherPriority.ContextIdle));
    }

    /// <summary>Name property returns "Dispatcher".</summary>
    [Test]
    public void DispatcherIdlingResource_Name_IsDispatcher()
    {
        using var resource = new DispatcherIdlingResource(Dispatcher.CurrentDispatcher);
        Assert.That(resource.Name, Is.EqualTo("Dispatcher"));
    }

    // ------------------------------------------------------------------ //
    //  DispatcherTimerResource
    // ------------------------------------------------------------------ //

    /// <summary>Stopped timer yields idle resource.</summary>
    [Test]
    public void DispatcherTimerResource_IdleWhenTimerStopped()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        using var resource = new DispatcherTimerResource(timer);
        Assert.That(resource.IsIdle, Is.True, "Stopped timer should yield idle resource.");
    }

    /// <summary>Running timer yields non-idle resource.</summary>
    [Test]
    public void DispatcherTimerResource_NotIdle_WhenTimerRunning()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Start();
        try
        {
            using var resource = new DispatcherTimerResource(timer);
            resource.Refresh();
            Assert.That(resource.IsIdle, Is.False, "Running timer should yield non-idle resource.");
        }
        finally
        {
            timer.Stop();
        }
    }

    /// <summary>Refresh transitions to idle after Stop.</summary>
    [Test]
    public void DispatcherTimerResource_Refresh_TransitionsToIdle_AfterStop()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Start();

        using var resource = new DispatcherTimerResource(timer);
        resource.Refresh(); // non-idle

        var events = new List<bool>();
        resource.IdleChanged += (_, e) => events.Add(e.IsIdle);

        timer.Stop();
        resource.Refresh(); // should transition to idle

        Assert.That(resource.IsIdle, Is.True);
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.True);
    }

    /// <summary>CheckPriority is ContextIdle.</summary>
    [Test]
    public void DispatcherTimerResource_HasCorrectCheckPriority()
    {
        var timer = new DispatcherTimer();
        using var resource = new DispatcherTimerResource(timer);
        Assert.That(resource.CheckPriority, Is.EqualTo(DispatcherPriority.ContextIdle));
    }

    // ------------------------------------------------------------------ //
    //  StoryboardResource
    // ------------------------------------------------------------------ //

    /// <summary>Storyboard not running yields idle resource.</summary>
    [Test]
    public void StoryboardResource_IdleByDefault_WhenNotRunning()
    {
        var sb = new Storyboard { Name = "TestSB" };
        using var resource = new StoryboardResource(sb);
        Assert.That(resource.IsIdle, Is.True, "Storyboard not running => idle.");
    }

    /// <summary>Storyboard marked initiallyRunning yields non-idle resource.</summary>
    [Test]
    public void StoryboardResource_NotIdle_WhenMarkedInitiallyRunning()
    {
        var sb = new Storyboard { Name = "RunningTestSB" };
        using var resource = new StoryboardResource(sb, initiallyRunning: true);
        Assert.That(resource.IsIdle, Is.False, "Storyboard marked initiallyRunning => not idle.");
    }

    /// <summary>CheckPriority is ContextIdle.</summary>
    [Test]
    public void StoryboardResource_HasCorrectCheckPriority()
    {
        var sb = new Storyboard();
        using var resource = new StoryboardResource(sb);
        Assert.That(resource.CheckPriority, Is.EqualTo(DispatcherPriority.ContextIdle));
    }

    /// <summary>Name contains the storyboard's Name.</summary>
    [Test]
    public void StoryboardResource_Name_ContainsStoryboardName()
    {
        var sb = new Storyboard { Name = "MySB" };
        using var resource = new StoryboardResource(sb);
        Assert.That(resource.Name, Does.Contain("MySB"));
    }

    // ------------------------------------------------------------------ //
    //  IdlingResourceRegistry -- AND-gate
    // ------------------------------------------------------------------ //

    /// <summary>Empty registry is idle.</summary>
    [Test]
    public void IdleGate_EmptyRegistry_IsIdle()
    {
        using var registry = new IdlingResourceRegistry();
        Assert.That(registry.IsIdle, Is.True, "Empty registry must be idle.");
    }

    /// <summary>Single idle resource keeps registry idle.</summary>
    [Test]
    public void IdleGate_SingleIdleResource_IsIdle()
    {
        using var registry = new IdlingResourceRegistry();
        var fake = new FakeIdlingResource("A", initiallyIdle: true);
        registry.Register(fake);

        Assert.That(registry.IsIdle, Is.True);
    }

    /// <summary>Single busy resource makes registry non-idle.</summary>
    [Test]
    public void IdleGate_SingleBusyResource_IsNotIdle()
    {
        using var registry = new IdlingResourceRegistry();
        var fake = new FakeIdlingResource("A", initiallyIdle: false);
        registry.Register(fake);

        Assert.That(registry.IsIdle, Is.False);
    }

    /// <summary>All idle resources keeps registry idle.</summary>
    [Test]
    public void IdleGate_AllIdleResources_IsIdle()
    {
        using var registry = new IdlingResourceRegistry();
        var a = new FakeIdlingResource("A", initiallyIdle: true);
        var b = new FakeIdlingResource("B", initiallyIdle: true);
        registry.Register(a);
        registry.Register(b);

        Assert.That(registry.IsIdle, Is.True);
    }

    /// <summary>AND-gate: registry becomes idle only when both resources are idle.</summary>
    [Test]
    public void IdleGate_WaitsForSlowest_BothMustBeIdle()
    {
        using var registry = new IdlingResourceRegistry();
        var fast = new FakeIdlingResource("Fast", initiallyIdle: false);
        var slow = new FakeIdlingResource("Slow", initiallyIdle: false);
        registry.Register(fast);
        registry.Register(slow);

        // Only the fast one becomes idle first.
        fast.SetIdle(true);
        Assert.That(registry.IsIdle, Is.False,
            "AND-gate: slow is still busy, registry must not be idle.");

        // Now the slow one also becomes idle.
        slow.SetIdle(true);
        Assert.That(registry.IsIdle, Is.True,
            "AND-gate: both idle, registry must be idle.");
    }

    /// <summary>IdleChanged fires when all resources become idle.</summary>
    [Test]
    public void IdleGate_RaisesIdleChangedEvent_WhenAllBecomesIdle()
    {
        using var registry = new IdlingResourceRegistry();
        var a = new FakeIdlingResource("A", initiallyIdle: false);
        var b = new FakeIdlingResource("B", initiallyIdle: false);
        registry.Register(a);
        registry.Register(b);

        var events = new List<bool>();
        registry.IdleChanged += (_, e) => events.Add(e.IsIdle);

        a.SetIdle(true);   // not yet -- b still busy
        b.SetIdle(true);   // now both idle => event

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.True);
    }

    /// <summary>IdleChanged fires when a resource transitions to busy.</summary>
    [Test]
    public void IdleGate_RaisesIdleChangedEvent_WhenOneBecomesBusy()
    {
        using var registry = new IdlingResourceRegistry();
        var a = new FakeIdlingResource("A", initiallyIdle: true);
        registry.Register(a);

        var events = new List<bool>();
        registry.IdleChanged += (_, e) => events.Add(e.IsIdle);

        a.SetIdle(false); // busy event
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.False);
    }

    /// <summary>Unregistering the only busy resource makes registry idle.</summary>
    [Test]
    public void IdleGate_Unregister_RemovesResource()
    {
        using var registry = new IdlingResourceRegistry();
        var a = new FakeIdlingResource("A", initiallyIdle: false);
        registry.Register(a);

        Assert.That(registry.IsIdle, Is.False);

        registry.Unregister(a);
        Assert.That(registry.IsIdle, Is.True,
            "After unregistering the only busy resource, registry should be idle.");
    }

    /// <summary>IdleChangedEventArgs.At is a recent UTC timestamp.</summary>
    [Test]
    public void IdleGate_IdleChangedEvent_IncludesTimestamp()
    {
        using var registry = new IdlingResourceRegistry();
        var a = new FakeIdlingResource("A", initiallyIdle: false);
        registry.Register(a);

        DateTimeOffset? stamp = null;
        registry.IdleChanged += (_, e) => stamp = e.At;

        a.SetIdle(true);

        Assert.That(stamp, Is.Not.Null);
        Assert.That(stamp!.Value, Is.GreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5)));
    }

    // ------------------------------------------------------------------ //
    //  Helper: controllable fake resource
    // ------------------------------------------------------------------ //

    private sealed class FakeIdlingResource : IIdlingResource
    {
        private int isIdle;

        public FakeIdlingResource(string name, bool initiallyIdle)
        {
            this.Name = name;
            this.isIdle = initiallyIdle ? 1 : 0;
        }

        public string Name { get; }

        public bool IsIdle => Volatile.Read(ref this.isIdle) == 1;

        public DispatcherPriority CheckPriority => DispatcherPriority.ContextIdle;

        public event EventHandler<IdleChangedEventArgs>? IdleChanged;

        public void SetIdle(bool idle)
        {
            int newValue = idle ? 1 : 0;
            int previous = Interlocked.Exchange(ref this.isIdle, newValue);
            if (previous != newValue)
            {
                this.IdleChanged?.Invoke(this, new IdleChangedEventArgs
                {
                    IsIdle = idle,
                    At = DateTimeOffset.UtcNow,
                });
            }
        }
    }
}
