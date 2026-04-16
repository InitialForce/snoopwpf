namespace SnoopWPF.Agent.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Engine;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// Unit tests for LocatorResolver path= form hierarchy walking (FX-M13).
///
/// Verifies that <c>path=WindowA\Button</c> does NOT match a Button whose
/// logical ancestor is WindowB, and that the correct Button IS found when
/// the ancestor matches.
///
/// Tests run on a dedicated STA dispatcher thread (no full Application needed).
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "NUnit [OneTimeTearDown] disposes registry after all tests.")]
public sealed class LocatorResolutionTests
{
    private Dispatcher dispatcher = null!;
    private Thread dispatcherThread = null!;
    private NodeRegistry registry = null!;

    [OneTimeSetUp]
    public void SetUpDispatcher()
    {
        var ready = new ManualResetEventSlim(false);
        this.dispatcherThread = new Thread(() =>
        {
            this.dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "LocatorResolutionTests-Dispatcher",
        };
        this.dispatcherThread.SetApartmentState(ApartmentState.STA);
        this.dispatcherThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        this.registry = new NodeRegistry(sweepInterval: TimeSpan.FromHours(1));
    }

    [OneTimeTearDown]
    public void TearDownDispatcher()
    {
        this.dispatcher.InvokeShutdown();
        this.dispatcherThread.Join(TimeSpan.FromSeconds(3));
        this.registry?.Dispose();
    }

    // -------------------------------------------------------------------------
    // path= hierarchy — FX-M13: walk all segments bottom-up
    // -------------------------------------------------------------------------

    /// <summary>
    /// path=StackPanel\Button resolves to the Button inside the StackPanel.
    /// </summary>
    [Test]
    public void Path_HierarchyMatch_ResolvesCorrectButton()
    {
        // Build: StackPanel (root) -> Button
        string? nodeId = null;
        Exception? ex = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            var button = new Button { Name = "myButton" };
            panel.Children.Add(button);

            // Ensure logical tree is set up.
            var resolver = new LocatorResolver(this.registry);
            var locator = WpfLocatorParser.Parse(@"path=StackPanel\Button");

            try
            {
                nodeId = resolver.Resolve(locator, panel);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });

        Assert.That(ex, Is.Null, $"Unexpected exception: {ex}");
        Assert.That(nodeId, Is.Not.Null.And.Not.Empty,
            "path=StackPanel\\Button should resolve to the Button inside StackPanel.");
    }

    /// <summary>
    /// path=WrongParent\Button does NOT match a Button whose parent is StackPanel.
    /// Before FX-M13 this would silently match because only the last segment was checked.
    /// </summary>
    [Test]
    public void Path_WrongParentSegment_DoesNotMatch()
    {
        Exception? caught = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            var button = new Button();
            panel.Children.Add(button);

            var resolver = new LocatorResolver(this.registry);
            // "WrongParent" does not match "StackPanel", so this must throw NodeNotFound.
            var locator = WpfLocatorParser.Parse(@"path=WrongParent\Button");

            try
            {
                resolver.Resolve(locator, panel);
            }
            catch (SnoopException e)
            {
                caught = e;
            }
        });

        Assert.That(caught, Is.Not.Null,
            "path=WrongParent\\Button should throw when the parent segment does not match.");
        var snoopEx = (SnoopException)caught!;
        Assert.That(
            snoopEx.Code == SnoopErrorCode.NodeNotFound || snoopEx.Code == SnoopErrorCode.LocatorAmbiguous,
            Is.True,
            $"Expected NodeNotFound or LocatorAmbiguous, got {snoopEx.Code}.");
    }

    /// <summary>
    /// A single-segment path= behaves like a type= match and resolves the first element
    /// of that type name in the tree.
    /// </summary>
    [Test]
    public void Path_SingleSegment_ResolvesFirstMatchingType()
    {
        string? nodeId = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            var button = new Button();
            panel.Children.Add(button);

            var resolver = new LocatorResolver(this.registry);
            var locator = WpfLocatorParser.Parse("path=Button");

            try
            {
                nodeId = resolver.Resolve(locator, panel);
            }
            catch
            {
                nodeId = null;
            }
        });

        Assert.That(nodeId, Is.Not.Null.And.Not.Empty,
            "Single-segment path=Button should resolve to the Button.");
    }

    // -------------------------------------------------------------------------
    // FX-N3: Gap 4 — LocatorResolver cap fires for all 4 locator forms
    // -------------------------------------------------------------------------

    /// <summary>
    /// When a tree has more than <c>MaxNewEntries</c> (100) nodes and none match the
    /// <c>automationId=</c> locator, the resolver must throw
    /// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/>
    /// before exhausting memory.
    ///
    /// The tree is built with 101 plain TextBlock children (no AutomationId set) so the
    /// cap fires at entry 100 without finding a match.
    /// </summary>
    [Test]
    public void AutomationId_Cap_ThrowsLocatorAmbiguous_ForWideTree()
    {
        SnoopException? caught = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            // 101 children — resolver will visit 100 (the cap) and throw LocatorAmbiguous.
            for (int i = 0; i < 101; i++)
            {
                panel.Children.Add(new TextBlock { Text = $"item-{i}" });
            }

            var resolver = new LocatorResolver(this.registry);
            var locator = WpfLocatorParser.Parse("automationId=DOES_NOT_EXIST");

            try
            {
                resolver.Resolve(locator, panel);
            }
            catch (SnoopException ex)
            {
                caught = ex;
            }
        });

        Assert.That(caught, Is.Not.Null,
            "Resolver must throw for a non-existent automationId= in a tree with 101+ nodes.");
        Assert.That(
            caught!.Code,
            Is.EqualTo(SnoopErrorCode.LocatorAmbiguous),
            $"Expected LocatorAmbiguous, got {caught.Code}.");
    }

    /// <summary>
    /// When a tree has more than <c>MaxNewEntries</c> (100) nodes and none match the
    /// <c>type=</c> locator value, the resolver must throw
    /// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/>.
    ///
    /// The tree is built with 101 Button children; the locator asks for <c>type=NoSuchType</c>
    /// which matches nothing, so the cap fires at entry 100.
    /// </summary>
    [Test]
    public void TypeName_Cap_ThrowsLocatorAmbiguous_ForWideTree()
    {
        SnoopException? caught = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            for (int i = 0; i < 101; i++)
            {
                panel.Children.Add(new Button { Content = $"btn-{i}" });
            }

            var resolver = new LocatorResolver(this.registry);
            var locator = WpfLocatorParser.Parse("type=NoSuchTypeName");

            try
            {
                resolver.Resolve(locator, panel);
            }
            catch (SnoopException ex)
            {
                caught = ex;
            }
        });

        Assert.That(caught, Is.Not.Null,
            "Resolver must throw for a non-existent type= in a tree with 101+ nodes.");
        Assert.That(
            caught!.Code,
            Is.EqualTo(SnoopErrorCode.LocatorAmbiguous),
            $"Expected LocatorAmbiguous, got {caught.Code}.");
    }

    /// <summary>
    /// When a tree has more than <c>MaxNewEntries</c> (100) nodes and none match the
    /// <c>path=</c> locator hierarchy, the resolver must throw
    /// <see cref="SnoopException"/> with <see cref="SnoopErrorCode.LocatorAmbiguous"/>.
    ///
    /// The tree is built with 101 CheckBox children; the locator asks for
    /// <c>path=NoParent\CheckBox</c>. Each CheckBox matches the last segment but not the
    /// parent segment, so the cap fires at entry 100 without a full match.
    /// </summary>
    [Test]
    public void Path_Cap_ThrowsLocatorAmbiguous_ForWideTree()
    {
        SnoopException? caught = null;

        this.dispatcher.Invoke(() =>
        {
            var panel = new StackPanel();
            for (int i = 0; i < 101; i++)
            {
                panel.Children.Add(new CheckBox { Content = $"chk-{i}" });
            }

            var resolver = new LocatorResolver(this.registry);
            // path=NoParent\CheckBox: last segment (CheckBox) matches but parent (NoParent) never does.
            var locator = WpfLocatorParser.Parse(@"path=NoParent\CheckBox");

            try
            {
                resolver.Resolve(locator, panel);
            }
            catch (SnoopException ex)
            {
                caught = ex;
            }
        });

        Assert.That(caught, Is.Not.Null,
            "Resolver must throw for path=NoParent\\CheckBox in a tree with 101+ nodes.");
        Assert.That(
            caught!.Code,
            Is.EqualTo(SnoopErrorCode.LocatorAmbiguous),
            $"Expected LocatorAmbiguous, got {caught.Code}.");
    }
}
