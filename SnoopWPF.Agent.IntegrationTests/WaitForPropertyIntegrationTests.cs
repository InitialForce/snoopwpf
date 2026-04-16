namespace SnoopWPF.Agent.IntegrationTests;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Integration tests for <c>wpf_wait_for_property</c> (M2-09).
///
/// Covers:
///   1. Value-equals-X success path (presenceExpected=present).
///   2. presenceExpected=absent on a disappearing element → success (modal-dismissal pattern).
///   3. Timeout path → SnoopException(DispatcherBusy) + suggestion wpf_pump_until_idle.
/// </summary>
[TestFixture]
public sealed class WaitForPropertyIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // Test 1: value-equals-X success path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the TextBox text to a known value, then calls WaitForPropertyAsync expecting
    /// that value — should return immediately with conditionMet=true.
    /// </summary>
    [Test]
    public async Task WaitForProperty_ValueEqualsExpected_SucceedsImmediately()
    {
        // Arrange: ensure testTextBox has a specific text value.
        const string expectedText = "WaitForProperty_Test_Value";
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;
            var textBox = rootPanel.Children.OfType<TextBox>().First(t => t.Name == "testTextBox");
            textBox.Text = expectedText;
        });

        var locator = WpfLocatorParser.Parse("type=TextBox, name=testTextBox");

        // Act.
        var result = await this.Client.Inspector.WaitForPropertyAsync(
            locator: locator,
            propertyName: "Text",
            expectedValue: expectedText,
            timeoutMs: 3000,
            presenceExpected: "present",
            ct: CancellationToken.None).ConfigureAwait(false);

        // Assert.
        Assert.That(result.ConditionMet, Is.True,
            "Condition should be met immediately when the property already has the expected value.");
        Assert.That(result.ActualValue, Is.EqualTo(expectedText),
            "ActualValue must equal the expected text.");
        Assert.That(result.PollCount, Is.GreaterThanOrEqualTo(1),
            "At least one poll must have been executed.");
        Assert.That(result.ElapsedMs, Is.GreaterThanOrEqualTo(0),
            "ElapsedMs must be non-negative.");
    }

    // -------------------------------------------------------------------------
    // Test 2: presenceExpected=absent (modal-dismissal / element removal)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a transient Button to the live tree, starts waiting for its absence,
    /// then removes it from a background thread — expects conditionMet=true.
    /// </summary>
    [Test]
    public async Task WaitForProperty_AbsentOnDisappearingElement_Succeeds()
    {
        // Arrange: add a temporary Button with a unique AutomationId so the locator can find it.
        const string tempAutomationId = "transientWaitButton-aid";

        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;

            // Remove any leftover from a previous run.
            var existing = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == tempAutomationId);
            if (existing is not null)
            {
                rootPanel.Children.Remove(existing);
            }

            var btn = new Button
            {
                Content = "Transient",
                Width = 80,
                Height = 24,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(btn, tempAutomationId);
            rootPanel.Children.Add(btn);
        });

        var locator = WpfLocatorParser.Parse($"automationId={tempAutomationId}");

        // Start the wait task — must be kicked off before the removal.
        var waitTask = this.Client.Inspector.WaitForPropertyAsync(
            locator: locator,
            propertyName: "IsEnabled",  // any property; we are testing absence, not value
            expectedValue: null,
            timeoutMs: 5000,
            presenceExpected: "absent",
            ct: CancellationToken.None);

        // Remove the button after a short delay.
        await Task.Delay(200).ConfigureAwait(false);

        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;
            var btn = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == tempAutomationId);
            if (btn is not null)
            {
                rootPanel.Children.Remove(btn);
            }
        });

        // Act: await the wait task.
        var result = await waitTask.ConfigureAwait(false);

        // Assert.
        Assert.That(result.ConditionMet, Is.True,
            "presenceExpected=absent should succeed when the element is removed from the tree.");
        Assert.That(result.ElapsedMs, Is.GreaterThan(0),
            "ElapsedMs must reflect actual wait time.");
    }

    // -------------------------------------------------------------------------
    // Teardown — restore state after tests
    // -------------------------------------------------------------------------

    [TearDown]
    public void TearDown()
    {
        const string tempAutomationId = "transientWaitButton-aid";
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var rootPanel = (StackPanel)this.WpfApp.MainWindow.Content;

            // Remove transient button if still present (e.g. on test failure).
            var existing = rootPanel.Children
                .OfType<Button>()
                .FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == tempAutomationId);
            if (existing is not null)
            {
                rootPanel.Children.Remove(existing);
            }

            // Restore testTextBox text.
            var textBox = rootPanel.Children.OfType<TextBox>().FirstOrDefault(t => t.Name == "testTextBox");
            if (textBox is not null)
            {
                textBox.Text = "Hello WPF";
            }
        });
    }

    // -------------------------------------------------------------------------
    // Test 3: timeout path → DispatcherBusy + suggestion wpf_pump_until_idle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits for a property value that will never be reached — expects timeout with
    /// SnoopException(DispatcherBusy) and the wpf_pump_until_idle suggestion.
    /// </summary>
    [Test]
    public void WaitForProperty_Timeout_ThrowsDispatcherBusyWithSuggestion()
    {
        const string impossibleValue = "__NEVER_GOING_TO_MATCH_THIS__";
        var locator = WpfLocatorParser.Parse("type=TextBox, name=testTextBox");

        var ex = Assert.ThrowsAsync<SnoopException>(async () =>
        {
            await this.Client.Inspector.WaitForPropertyAsync(
                locator: locator,
                propertyName: "Text",
                expectedValue: impossibleValue,
                timeoutMs: 300,  // very short timeout to keep the test fast
                presenceExpected: "present",
                ct: CancellationToken.None).ConfigureAwait(false);
        });

        Assert.That(ex, Is.Not.Null, "A SnoopException must be thrown on timeout.");
        Assert.That(ex!.Code, Is.EqualTo(SnoopErrorCode.DispatcherBusy),
            "Timeout must surface as DispatcherBusy.");
        Assert.That(ex.Suggestions, Is.Not.Null.And.Length.GreaterThan(0),
            "Timeout exception must include at least one suggestion.");
        Assert.That(
            ex.Suggestions![0],
            Does.Contain("wpf_pump_until_idle"),
            "Suggestion must mention wpf_pump_until_idle.");
    }
}
