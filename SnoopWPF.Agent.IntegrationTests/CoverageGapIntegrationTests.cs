namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Coverage-gap closure tests (M2-16, bd-2co).
///
/// Each test corresponds to a scenario family marked "shim covers" in
/// COVERAGE-GAP-AUDIT.md (M0-06) and validates that the shim primitive
/// already present in the Layer-1 tool surface correctly handles the scenario.
///
/// Test filter used by acceptance criterion:
///   dotnet test --filter "FullyQualifiedName~CoverageGap"
/// </summary>
/// <remarks>
/// Scenario families validated here:
/// <list type="bullet">
///   <item>
///     <b>Family 10 (UserSelection)</b> — <c>wpf_set_text_value</c> on a search TextBox
///     triggers a list filter. Tests that setting text to "Item T" on a 3-item ListBox
///     causes the value to be stored; the consumer of the text (filtering) is app-side.
///     Gap item: §12.3 "Virtualized-list scroll" (the search path avoids raw scroll).
///   </item>
///   <item>
///     <b>Family 14 (Analysis/QuickStart)</b> — <c>wpf_select_item</c> on a ComboBox.
///     Dropdown expand + item select maps to <c>wpf_expand_collapse</c> + <c>wpf_select_item</c>.
///     Camera list is small (debug cameras, not hardware-backed). Gap item: §12.3 "Multi-product launch".
///   </item>
///   <item>
///     <b>Family 08 (License)</b> — <c>wpf_wait_for_property(presenceExpected=absent)</c>
///     models the <c>DismissLicenseIfPresent()</c> shim helper (MC §8 item 4).
///     A transient Button is added then removed; the wait resolves to conditionMet=true.
///     Gap item: §12.3 "License-dialog detection in launch flow".
///   </item>
///   <item>
///     <b>§12.3 Slider normalization</b> — <c>wpf_set_slider_value(normalized=true)</c>
///     on <c>testSlider</c> (Minimum=0, Maximum=100). Input fraction 0.75 → expected value 75.
///     Gap item: §12.3 "Slider value setter + range normalization" (no active scenario today;
///     required when Analysis/Metric.feature stubs are implemented).
///   </item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class CoverageGapIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string?> FindNodeIdByNameAsync(string elementName)
    {
        var tree = await this.Client.GetVisualTreeAsync(maxDepth: 10).ConfigureAwait(false);
        var all = Flatten(tree.Root);
        return all.FirstOrDefault(
                n => n.Name != null &&
                     n.Name.Equals(elementName, StringComparison.Ordinal))
            ?.NodeId;
    }

    private static System.Collections.Generic.List<NodeDto> Flatten(NodeDto root)
    {
        var result = new System.Collections.Generic.List<NodeDto>();
        var queue = new System.Collections.Generic.Queue<NodeDto>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // CoverageGap_Family10_SetTextValue_SearchFilter
    // Scenario family: UserSelection (SCENARIO FAMILY 10)
    // Shim tool: wpf_set_text_value
    // §12.3 gap: Virtualized-list scroll + partial-text match (avoided by search filter)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that <c>wpf_set_text_value</c> correctly sets the Text property on a
    /// TextBox to a search-filter value, and that the change is reflected back in the
    /// new value. This models the UserSelection search-box step where typing triggers
    /// a list filter — the search-fill itself is the shim primitive under test.
    /// </summary>
    [Test]
    public async Task CoverageGap_Family10_SetTextValue_SearchFilter_SetsTextAndReportsStateChanged()
    {
        // Arrange.
        var nodeId = await this.FindNodeIdByNameAsync("testTextBox").ConfigureAwait(false);
        Assert.That(nodeId, Is.Not.Null, "testTextBox must be present in the visual tree.");

        const string searchQuery = "Smith";

        // Act — models "fill search box with 'Smith'" step.
        var result = await this.Client.Inspector
            .SetTextValueAsync(nodeId!, searchQuery, ct: CancellationToken.None)
            .ConfigureAwait(false);

        // Assert.
        Assert.That(result.Success, Is.True,
            "wpf_set_text_value must succeed on a TextBox (Family 10 search-filter path).");
        Assert.That(result.NewValue, Is.EqualTo(searchQuery),
            "NewValue must equal the search query after SetTextValue.");
        Assert.That(result.StateChanged, Is.True,
            "StateChanged must be true when text changes (non-empty filter applied).");
    }

    // -------------------------------------------------------------------------
    // CoverageGap_Family14_SelectItem_Dropdown
    // Scenario family: Analysis/QuickStart (SCENARIO FAMILY 14)
    // Shim tool: wpf_select_item
    // §12.3 gap: (none for this scenario — dropdown + select fully shimable)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that <c>wpf_select_item</c> can select a camera item by partial text in a
    /// ComboBox, modelling the Analysis/QuickStart "select debug camera" step (scenario 02/03).
    /// The camera list is small (software cameras), so no virtualization edge case applies.
    /// </summary>
    [Test]
    public async Task CoverageGap_Family14_SelectItem_Dropdown_SelectsByPartialText()
    {
        // Arrange — testComboBox contains "Apple", "Banana", "Cherry".
        var nodeId = await this.FindNodeIdByNameAsync("testComboBox").ConfigureAwait(false);
        Assert.That(nodeId, Is.Not.Null, "testComboBox must be present in the visual tree.");

        // Act — models "select camera 'Ban'" partial match step.
        var result = await this.Client.Inspector
            .SelectItemAsync(nodeId!, "Ban", ct: CancellationToken.None)
            .ConfigureAwait(false);

        // Assert.
        Assert.That(result.Success, Is.True,
            "wpf_select_item with partial text 'Ban' must succeed on testComboBox.");
        Assert.That(result.StateChanged, Is.True,
            "StateChanged must be true when a new item is selected.");

        // Verify the actual SelectedItem via the dispatcher.
        string? selectedText = null;
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;
            var combo = rootPanel.Children.OfType<ComboBox>().FirstOrDefault(c => c.Name == "testComboBox");
            selectedText = combo?.SelectedItem?.ToString();
        });

        Assert.That(selectedText, Is.EqualTo("Banana"),
            "After selecting 'Ban', SelectedItem must be 'Banana'.");
    }

    // -------------------------------------------------------------------------
    // CoverageGap_Family08_WaitForProperty_LicenseDialogAbsent
    // Scenario family: License (SCENARIO FAMILY 08)
    // Shim tool: wpf_wait_for_property(presenceExpected=absent)
    // §12.3 gap: License-dialog detection in launch flow (DismissLicenseIfPresent)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates <c>wpf_wait_for_property(presenceExpected=absent)</c> as the shim
    /// implementation of <c>DismissLicenseIfPresent()</c> (MC §8 item 4).
    /// A transient element (simulating a license dialog) is added to the visual tree,
    /// a wait-for-absent is started, then the element is removed — expects conditionMet=true.
    /// </summary>
    [Test]
    public async Task CoverageGap_Family08_WaitForProperty_LicenseDialogAbsent_SucceedsOnElementRemoval()
    {
        // Arrange: add a transient Button simulating a license dialog presence indicator.
        const string licenseDialogAid = "coverageGap-licenseDialogSim-aid";

        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;

            // Clean up any leftover from a previous run.
            var existing = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(b) == licenseDialogAid);
            if (existing is not null)
            {
                rootPanel.Children.Remove(existing);
            }

            var simulatedDialog = new Button
            {
                Content = "Simulated License Dialog",
                Width = 200,
                Height = 32,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(simulatedDialog, licenseDialogAid);
            rootPanel.Children.Add(simulatedDialog);
        });

        var locator = WpfLocatorParser.Parse($"automationId={licenseDialogAid}");

        // Act: start waiting for absence (models DismissLicenseIfPresent check step).
        var waitTask = this.Client.Inspector.WaitForPropertyAsync(
            locator: locator,
            propertyName: "IsEnabled",
            expectedValue: null,
            timeoutMs: 5000,
            presenceExpected: "absent",
            ct: CancellationToken.None);

        // Remove the simulated dialog after a short delay (models user dismissing).
        await Task.Delay(150).ConfigureAwait(false);

        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;
            var btn = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(b) == licenseDialogAid);
            if (btn is not null)
            {
                rootPanel.Children.Remove(btn);
            }
        });

        var result = await waitTask.ConfigureAwait(false);

        // Assert.
        Assert.That(result.ConditionMet, Is.True,
            "presenceExpected=absent must succeed when license-dialog element is removed (Family 08).");
        Assert.That(result.ElapsedMs, Is.GreaterThan(0),
            "ElapsedMs must be positive (wait actually polled).");
    }

    // -------------------------------------------------------------------------
    // CoverageGap_Slider_NormalizedRange
    // Scenario family: §12.3 gap "Slider value setter + range normalization"
    // Shim tool: wpf_set_slider_value(normalized=true)
    // (No active MC SpecFlow scenario yet — required when Metric.feature is implemented)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates <c>wpf_set_slider_value(normalized=true)</c> on a Slider with
    /// Minimum=0, Maximum=100. Input fraction 0.75 must produce Value=75.
    /// This is the <c>SetSlider(id, 0..1)</c> shim helper per MC §8 item 1.
    /// </summary>
    [Test]
    public async Task CoverageGap_Slider_NormalizedRange_SetsFractionToCorrectAbsoluteValue()
    {
        // Arrange — testSlider: Minimum=0, Maximum=100, initial Value=50.
        var nodeId = await this.FindNodeIdByNameAsync("testSlider").ConfigureAwait(false);
        Assert.That(nodeId, Is.Not.Null,
            "testSlider must be present in the visual tree (added in M2-16).");

        // Act — set to 75% of range (normalized fraction 0.75 → absolute 75 on 0..100).
        var result = await this.Client.SetSliderValueAsync(
            nodeId!,
            value: 0.75,
            normalized: true,
            ct: CancellationToken.None).ConfigureAwait(false);

        // Assert.
        Assert.That(result.Success, Is.True,
            "wpf_set_slider_value with normalized=true must succeed on a Slider.");
        Assert.That(result.StateChanged, Is.True,
            "StateChanged must be true (value changed from 50 to 75).");
        Assert.That(result.NewValue, Is.EqualTo("75"),
            "NewValue must be '75' — fraction 0.75 on range 0..100.");
        Assert.That(result.PreviousValue, Is.EqualTo("50"),
            "PreviousValue must be '50' (initial value of testSlider).");
    }

    /// <summary>
    /// Validates that <c>wpf_set_slider_value(normalized=false)</c> sets an absolute
    /// value directly, clamped to [Minimum, Maximum].
    /// </summary>
    [Test]
    public async Task CoverageGap_Slider_AbsoluteValue_SetsDirectly()
    {
        // Arrange — reset slider to known state first.
        var nodeId = await this.FindNodeIdByNameAsync("testSlider").ConfigureAwait(false);
        Assert.That(nodeId, Is.Not.Null, "testSlider must be present.");

        // Reset to 50.
        await this.Client.SetSliderValueAsync(nodeId!, 50.0, normalized: false).ConfigureAwait(false);

        // Act — set absolute value 30.
        var result = await this.Client.SetSliderValueAsync(
            nodeId!,
            value: 30.0,
            normalized: false,
            ct: CancellationToken.None).ConfigureAwait(false);

        // Assert.
        Assert.That(result.Success, Is.True, "wpf_set_slider_value(normalized=false) must succeed.");
        Assert.That(result.NewValue, Is.EqualTo("30"),
            "NewValue must be '30' for absolute-mode input.");
    }

    /// <summary>
    /// Validates that <c>wpf_set_slider_value(normalized=true, value=1.0)</c> sets the slider
    /// to its Maximum. Guards the boundary/clamping logic in the range-normalization path.
    /// </summary>
    [Test]
    public async Task CoverageGap_Slider_NormalizedRange_Fraction1_SetsToMaximum()
    {
        var nodeId = await this.FindNodeIdByNameAsync("testSlider").ConfigureAwait(false);
        Assert.That(nodeId, Is.Not.Null, "testSlider must be present.");

        var result = await this.Client.SetSliderValueAsync(
            nodeId!,
            value: 1.0,
            normalized: true,
            ct: CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NewValue, Is.EqualTo("100"),
            "Fraction 1.0 on range 0..100 must yield Maximum=100.");
    }

    // -------------------------------------------------------------------------
    // Teardown
    // -------------------------------------------------------------------------

    [TearDown]
    public void TearDown()
    {
        const string licenseDialogAid = "coverageGap-licenseDialogSim-aid";

        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;

            // Remove simulated license dialog if still present.
            var btn = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(b) == licenseDialogAid);
            if (btn is not null)
            {
                rootPanel.Children.Remove(btn);
            }

            // Restore testTextBox.
            var textBox = rootPanel.Children.OfType<TextBox>().FirstOrDefault(t => t.Name == "testTextBox");
            if (textBox is not null)
            {
                textBox.Text = "Hello WPF";
            }

            // Reset testComboBox selection.
            var combo = rootPanel.Children.OfType<ComboBox>().FirstOrDefault(c => c.Name == "testComboBox");
            if (combo is not null)
            {
                combo.SelectedIndex = -1;
            }

            // Reset testSlider to initial value.
            var slider = rootPanel.Children.OfType<Slider>().FirstOrDefault(s => s.Name == "testSlider");
            if (slider is not null)
            {
                slider.Value = 50.0;
            }
        });
    }
}
