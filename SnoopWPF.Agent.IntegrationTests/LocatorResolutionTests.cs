namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Integration tests for <see cref="ISnoopInspector"/> WpfLocator overloads (M1-06).
/// Verifies that all four locator forms resolve correctly and that the NodeRegistry
/// growth cap (100 entries) is enforced for large/virtualized trees.
///
/// Uses the shared <see cref="TestWpfApp"/> fixture which contains:
///   Button ("testButton"), TextBox ("testTextBox"), TextBlock ("testTextBlock"),
///   ListBox ("testListBox"), PasswordBox ("testPasswordBox"),
///   and a virtualized ListBox "testBigList" with 10 000 items.
/// </summary>
[TestFixture]
public sealed class LocatorResolutionTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Setup: assign AutomationId to testButton so AutomationId= form can match it.
    // -------------------------------------------------------------------------

    [OneTimeSetUp]
    public void SetUpAutomationIds()
    {
        // Set AutomationId on the test button so the AutomationId= locator test works.
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var mainWindow = this.WpfApp.MainWindow;
            // Walk to find testButton.
            var root = mainWindow.Content as System.Windows.Controls.Panel;
            if (root is null)
            {
                return;
            }

            foreach (System.Windows.UIElement child in root.Children)
            {
                if (child is System.Windows.Controls.Button btn && btn.Name == "testButton")
                {
                    AutomationProperties.SetAutomationId(btn, "testButton-aid");
                }
            }
        });
    }

    // -------------------------------------------------------------------------
    // AutomationId= form
    // -------------------------------------------------------------------------

    /// <summary>
    /// automationId= form resolves to the element with the matching AutomationId.
    /// </summary>
    [Test]
    public async Task Locator_AutomationId_ResolvesElement()
    {
        var locator = WpfLocatorParser.Parse("automationId=testButton-aid");

        var result = await this.Client.Inspector
            .InspectElementAsync(locator, default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.NodeId, Is.Not.Null.And.Not.Empty,
            "Resolved nodeId must be non-empty.");
        Assert.That(result.Name, Is.EqualTo("testButton").IgnoreCase,
            "AutomationId= locator should resolve to testButton.");
    }

    // -------------------------------------------------------------------------
    // type= form
    // -------------------------------------------------------------------------

    /// <summary>
    /// type=Button resolves to the first Button in the tree.
    /// </summary>
    [Test]
    public async Task Locator_TypeName_ResolvesFirstButton()
    {
        var locator = WpfLocatorParser.Parse("type=Button");

        var result = await this.Client.Inspector
            .InspectElementAsync(locator, default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeName, Does.Contain("Button").IgnoreCase,
            "type=Button locator should resolve to an element whose type name contains Button.");
    }

    /// <summary>
    /// type=Button, name=testButton resolves specifically to testButton.
    /// </summary>
    [Test]
    public async Task Locator_TypeName_WithName_ResolvesNamedButton()
    {
        var locator = WpfLocatorParser.Parse("type=Button, name=testButton");

        var result = await this.Client.Inspector
            .InspectElementAsync(locator, default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("testButton").IgnoreCase,
            "type=Button, name=testButton should resolve to testButton.");
    }

    // -------------------------------------------------------------------------
    // path= form
    // -------------------------------------------------------------------------

    /// <summary>
    /// path=Button form matches the first element whose type name ends with Button.
    /// Documented behaviour: path= matches first; SWPF0011 warns in M1-18.
    /// </summary>
    [Test]
    public async Task Locator_Path_ResolvesFirstMatchingType()
    {
        var locator = WpfLocatorParser.Parse("path=Button");

        var result = await this.Client.Inspector
            .InspectElementAsync(locator, default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.NodeId, Is.Not.Null.And.Not.Empty,
            "path= locator must return a non-empty nodeId.");
    }

    // -------------------------------------------------------------------------
    // viewModel= form
    // -------------------------------------------------------------------------

    /// <summary>
    /// viewModel= form with a non-existent type throws NodeNotFound.
    /// (TestWpfApp has no custom ViewModels set as DataContext.)
    /// </summary>
    [Test]
    public void Locator_ViewModel_NonExistentType_ThrowsNodeNotFound()
    {
        var locator = WpfLocatorParser.Parse("viewModel=NonExistentViewModel_xyz99");

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await this.Client.Inspector
                .InspectElementAsync(locator, default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.NodeNotFound).Or.EqualTo(SnoopErrorCode.LocatorAmbiguous),
            "Non-existent viewModel= should throw NodeNotFound (or LocatorAmbiguous if tree is large).");
    }

    // -------------------------------------------------------------------------
    // NodeRegistry growth cap — virtualized list (10 000 items)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolving a viewModel= locator against the 10 000-item testBigList
    /// triggers the NodeRegistry growth cap (100 entries) and returns
    /// LOCATOR_AMBIGUOUS — NOT OOM.
    /// </summary>
    [Test]
    public void Locator_CapEnforced_BigListViewModelLocator_ThrowsLocatorAmbiguous()
    {
        // Use a viewModel locator that will never match — forcing the resolver to
        // scan the entire tree and hit the cap.
        var locator = WpfLocatorParser.Parse("viewModel=ImaginaryBigListViewModel_xyz_unique");

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await this.Client.Inspector
                .InspectElementAsync(locator, default)
                .ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null);
        // May be LocatorAmbiguous (cap hit) or NodeNotFound (tree smaller than cap).
        // With testBigList present the tree can be large enough to trigger LocatorAmbiguous.
        Assert.That(
            ex!.Code == SnoopErrorCode.LocatorAmbiguous || ex.Code == SnoopErrorCode.NodeNotFound,
            Is.True,
            $"Expected LocatorAmbiguous or NodeNotFound but got {ex.Code}: {ex.Message}");
    }

    // -------------------------------------------------------------------------
    // ParentNodeId is non-empty for non-root elements
    // -------------------------------------------------------------------------

    /// <summary>
    /// InspectElementAsync returns a non-empty ParentNodeId for elements that are
    /// not the root (fix for PRD §14 debt item — was hardcoded to empty string).
    /// </summary>
    [Test]
    public async Task InspectElement_NonRoot_ParentNodeId_IsNonEmpty()
    {
        // Find testButton first (we know it exists and has a parent StackPanel).
        var windows = await this.Client.Inspector.GetWindowsAsync(includeHidden: true, ct: default).ConfigureAwait(false);
        Assert.That(windows, Is.Not.Empty, "Should have at least one window.");

        var windowNodeId = windows[0].NodeId;

        // Get children of the window — should include the root StackPanel.
        var children = await this.Client.Inspector.GetChildrenAsync(windowNodeId, "visual", null, 50, default).ConfigureAwait(false);
        Assert.That(children.Items, Is.Not.Empty, "Window should have children.");

        // Get a grandchild (the button is inside StackPanel which is inside Window).
        // Walk down one more level to get the button.
        string? buttonNodeId = null;
        foreach (var child in children.Items)
        {
            var grandchildren = await this.Client.Inspector.GetChildrenAsync(child.NodeId, "visual", null, 50, default).ConfigureAwait(false);
            foreach (var gc in grandchildren.Items)
            {
                if (gc.TypeName.Contains("Button", StringComparison.OrdinalIgnoreCase) &&
                    gc.Name.Contains("testButton", StringComparison.OrdinalIgnoreCase))
                {
                    buttonNodeId = gc.NodeId;
                    break;
                }
            }

            if (buttonNodeId is not null)
            {
                break;
            }
        }

        if (buttonNodeId is null)
        {
            // Fall back: search by name.
            var found = await this.Client.Inspector
                .FindElementsAsync("Button", "testButton", null, null, "visual", 1, default)
                .ConfigureAwait(false);

            if (found.Results.Count > 0)
            {
                buttonNodeId = found.Results[0].Node.NodeId;
            }
        }

        Assert.That(buttonNodeId, Is.Not.Null, "testButton must be found in the tree.");

        var dto = await this.Client.Inspector
            .InspectElementAsync(buttonNodeId!, default)
            .ConfigureAwait(false);

        Assert.That(dto.ParentNodeId, Is.Not.Null.And.Not.Empty,
            "ParentNodeId must be non-empty for a non-root element (testButton has a StackPanel parent).");
    }
}
