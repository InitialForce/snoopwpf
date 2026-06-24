namespace SnoopWPF.Agent.Tests.Engine;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine;

/// <summary>
/// VCOUNT (DESKTOP-11837): GetChildrenAsync builds children from the visual tree, so a
/// virtualized ItemsControl reports only its realized containers as TotalCount and looks
/// fully walked when it is not. These tests verify the advisory fields added to
/// <see cref="CursorPage{T}"/>:
/// <list type="bullet">
///   <item><see cref="CursorPage{T}.ItemHostCount"/> equals the true <c>ItemsControl.Items.Count</c>.</item>
///   <item><see cref="CursorPage{T}.Advisory"/> is set (and names <c>wpf_get_list_items</c>)
///   when logical items exceed realized visual children.</item>
///   <item>The page's <see cref="CursorPage{T}.TotalCount"/> / Items still reflect ONLY the
///   realized visual children — pagination semantics are unchanged.</item>
///   <item>A non-ItemsControl target leaves both new fields null.</item>
/// </list>
/// <para>
/// Harness note: the inspector sets a generous <c>DispatcherAcceptanceTimeoutMs</c> plus the
/// established one-time dispatcher warmup so a contended runner does not trip a spurious
/// DispatcherBusy. Reliably forcing a VirtualizingStackPanel to
/// virtualize headlessly (no real render surface) is not guaranteed, so the virtualized
/// case asserts the SEAM invariants — ItemHostCount == Items.Count and Advisory-when-gap —
/// rather than a specific realized-container count.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class GetChildrenItemHostCountTests : IDisposable
{
    private Thread? staThread;
    private Dispatcher? staDispatcher;
    private readonly ManualResetEventSlim dispatcherReady = new(initialState: false);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        this.staThread = new Thread(this.RunDispatcher)
        {
            Name = "GetChildrenVCount-STA",
            IsBackground = true,
        };
        this.staThread.SetApartmentState(ApartmentState.STA);
        this.staThread.Start();

        if (!this.dispatcherReady.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("STA Dispatcher did not start in time.");
        }

        // One-time warmup: JIT the Send-priority invoke path and start pumping so the
        // engine's acceptance window is not tripped by cold-start cost on a loaded runner.
        this.staDispatcher!.InvokeAsync(() => { }, DispatcherPriority.Send)
            .Task.Wait(TimeSpan.FromSeconds(10));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        this.staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
        this.staThread?.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose() => this.dispatcherReady.Dispose();

    private void RunDispatcher()
    {
        this.staDispatcher = Dispatcher.CurrentDispatcher;
        this.dispatcherReady.Set();
        Dispatcher.Run();
    }

    private SnoopInspector CreateInspector(DependencyObject root) =>
        new SnoopInspector(
            dispatcher: this.staDispatcher!,
            rootTarget: root,
            options: new SnoopInspectorOptions
            {
                // A generous acceptance window plus the warmup above keeps this test off the
                // flake line on a CPU-contended runner (the hardcoded 500ms default trips a
                // spurious DispatcherBusy when the STA thread is starved mid-test).
                DispatcherAcceptanceTimeoutMs = 5_000,
                TimeoutMs = 10_000,
                EnableMutation = false,
                EnableRedaction = false,
            });

    private static string GetRootNodeId(SnoopInspector inspector)
    {
        var tree = inspector
            .GetVisualTreeAsync(
                rootNodeId: (string?)null,
                maxDepth: 1,
                treeType: "visual",
                includeProperties: null,
                ct: default)
            .GetAwaiter().GetResult();

        return tree.Root.NodeId;
    }

    // ── Virtualized item host: true count surfaced, advisory set, pagination unchanged ──

    [Test]
    public void VirtualizedItemsControl_SurfacesTrueItemCount_AndAdvisory_WithoutCorruptingPagination()
    {
        const int itemCount = 200;
        const double constrainedHeight = 60.0; // ~3 rows tall — most containers stay unrealized.

        var listBox = this.staDispatcher!.Invoke(() =>
        {
            var lb = new ListBox
            {
                Height = constrainedHeight,
                Width = 120,
            };

            // Explicitly virtualizing with recycling so a constrained viewport leaves most
            // containers unrealized in the visual tree.
            VirtualizingStackPanel.SetIsVirtualizing(lb, true);
            VirtualizingStackPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(lb, true);

            for (int i = 0; i < itemCount; i++)
            {
                lb.Items.Add($"item-{i}");
            }

            // Best-effort: apply template + a constrained measure/arrange so the generator
            // realizes only a handful of containers. Headless (no real PresentationSource)
            // this may realize zero — which is fine; the seam invariants below still hold.
            lb.ApplyTemplate();
            lb.Measure(new Size(120, constrainedHeight));
            lb.Arrange(new Rect(0, 0, 120, constrainedHeight));
            lb.UpdateLayout();

            return lb;
        });

        using var inspector = this.CreateInspector(listBox);
        var nodeId = GetRootNodeId(inspector);

        CursorPage<NodeDto> page = inspector
            .GetChildrenAsync(nodeId, treeType: "visual", cursor: null, take: 200, ct: default)
            .GetAwaiter().GetResult();

        // True logical item count is surfaced regardless of realization.
        Assert.That(page.ItemHostCount, Is.EqualTo(itemCount),
            "ItemHostCount must equal the true ItemsControl.Items.Count.");

        // Pagination still describes only the realized visual children, never the logical count.
        Assert.That(page.TotalCount, Is.LessThan(itemCount),
            "TotalCount must reflect realized visual children only, not the logical item count.");
        Assert.That(page.Items.Count, Is.LessThanOrEqualTo(page.TotalCount),
            "Items in this page cannot exceed the realized visual-children TotalCount.");

        // Because realized < logical, the advisory must be present and point at the right tool.
        Assert.That(page.Advisory, Is.Not.Null,
            "Advisory must be set when logical items exceed realized visual children.");
        Assert.That(page.Advisory, Does.Contain("wpf_get_list_items"),
            "Advisory must direct the caller to wpf_get_list_items for the full item set.");
    }

    // ── Non-virtualized, non-ItemsControl target: both advisory fields stay null ──

    [Test]
    public void NonItemsControl_LeavesItemHostCountAndAdvisoryNull()
    {
        var grid = this.staDispatcher!.Invoke(() =>
        {
            var g = new Grid();
            g.Children.Add(new Button());
            g.Children.Add(new TextBlock());
            g.Children.Add(new Border());
            g.Measure(new Size(200, 200));
            g.Arrange(new Rect(0, 0, 200, 200));
            g.UpdateLayout();
            return g;
        });

        using var inspector = this.CreateInspector(grid);
        var nodeId = GetRootNodeId(inspector);

        CursorPage<NodeDto> page = inspector
            .GetChildrenAsync(nodeId, treeType: "visual", cursor: null, take: 50, ct: default)
            .GetAwaiter().GetResult();

        Assert.That(page.ItemHostCount, Is.Null,
            "A non-ItemsControl target must leave ItemHostCount null.");
        Assert.That(page.Advisory, Is.Null,
            "A non-ItemsControl target must leave Advisory null.");

        // Sanity: the grid's three direct visual children are reported normally.
        Assert.That(page.TotalCount, Is.EqualTo(3),
            "Grid with three children must report three visual children.");
    }
}
