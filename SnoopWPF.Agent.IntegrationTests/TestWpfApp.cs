namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>
/// Minimal relay command implementation for integration test fixtures.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

#pragma warning disable CS0067 // Event never used — ICommand requires the declaration
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => this.canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => this.execute(parameter);
}

/// <summary>
/// Creates a minimal WPF Application and Window on a dedicated STA thread
/// for use in integration tests. Provides known test elements for inspection.
/// Dispose to shut down the STA thread and all WPF resources.
/// </summary>
/// <remarks>
/// WPF allows only one <see cref="Application"/> per AppDomain. Use the shared
/// singleton via <see cref="IntegrationTestFixture.WpfApp"/> — do not create more
/// than one instance per test run.
///
/// Tests interact with the hosted application via the <see cref="Dispatcher"/>.
/// </remarks>
public sealed class TestWpfApp : IDisposable
{
    private readonly Thread staThread;
    private readonly ManualResetEventSlim readyEvent = new(initialState: false);

    private Application? app;
    private Window? mainWindow;
    private Dispatcher? dispatcher;
    private Exception? startupException;
    private bool disposed;

    /// <summary>
    /// Gets the WPF <see cref="System.Windows.Threading.Dispatcher"/> for the hosted application.
    /// All WPF object access must go through this dispatcher.
    /// </summary>
    public Dispatcher Dispatcher => this.dispatcher
        ?? throw new InvalidOperationException("TestWpfApp is not yet initialized.");

    /// <summary>
    /// Gets the hosted <see cref="System.Windows.Application"/> instance.
    /// </summary>
    public Application App => this.app
        ?? throw new InvalidOperationException("TestWpfApp is not yet initialized.");

    /// <summary>
    /// Gets the main test <see cref="Window"/>.
    /// </summary>
    public Window MainWindow => this.mainWindow
        ?? throw new InvalidOperationException("TestWpfApp is not yet initialized.");

    /// <summary>
    /// Creates and starts the WPF Application on an STA thread.
    /// Blocks until the Application is initialized and the main window is shown.
    /// </summary>
    /// <param name="startupTimeoutMs">
    /// Timeout in milliseconds to wait for Application startup. Defaults to 10 000 ms.
    /// </param>
    /// <exception cref="TimeoutException">
    /// Thrown if the Application does not start within <paramref name="startupTimeoutMs"/>.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Re-throws any exception raised during startup on the STA thread.
    /// </exception>
    public TestWpfApp(int startupTimeoutMs = 10_000)
    {
        this.staThread = new Thread(this.RunApplication)
        {
            Name = "TestWpfApp-STA",
            IsBackground = true,
        };
        this.staThread.SetApartmentState(ApartmentState.STA);
        this.staThread.Start();

        if (!this.readyEvent.Wait(startupTimeoutMs))
        {
            throw new TimeoutException(
                $"TestWpfApp did not initialize within {startupTimeoutMs} ms.");
        }

        if (this.startupException != null)
        {
            throw new AggregateException("TestWpfApp startup failed.", this.startupException);
        }
    }

    /// <summary>
    /// Shuts down the WPF Application and the STA thread.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        if (this.dispatcher != null && !this.dispatcher.HasShutdownStarted)
        {
            this.dispatcher.InvokeShutdown();
        }

        this.staThread.Join(timeout: TimeSpan.FromSeconds(5));
        this.readyEvent.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private — STA thread body
    // -------------------------------------------------------------------------

    private void RunApplication()
    {
        try
        {
            this.app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            this.dispatcher = Dispatcher.CurrentDispatcher;

            // Add known application-level resources so resource inspection tests always have
            // something to enumerate, regardless of the nodeId used in the query.
            this.app.Resources["TestBrush"] = new SolidColorBrush(Colors.CornflowerBlue);
            this.app.Resources["TestCornerRadius"] = new CornerRadius(4);

            this.app.Startup += (_, _) =>
            {
                this.mainWindow = this.BuildMainWindow();
                this.app.MainWindow = this.mainWindow;
                this.mainWindow.Show();
                this.readyEvent.Set();
            };

            this.app.Run();
        }
        catch (Exception ex)
        {
            this.startupException = ex;
            this.readyEvent.Set();
        }
    }

    private Window BuildMainWindow()
    {
        // Root: StackPanel named "rootPanel"
        var rootPanel = new StackPanel { Name = "rootPanel" };

        // Button: Name="testButton", Content="Click Me"
        var testButton = new Button
        {
            Name = "testButton",
            Content = "Click Me",
            Width = 120,
            Height = 32,
        };
        rootPanel.Children.Add(testButton);

        // TextBox: Name="testTextBox", Text="Hello WPF"
        var testTextBox = new TextBox
        {
            Name = "testTextBox",
            Text = "Hello WPF",
            Width = 200,
            Height = 24,
        };
        rootPanel.Children.Add(testTextBox);

        // TextBlock: Name="testTextBlock"
        var testTextBlock = new TextBlock
        {
            Name = "testTextBlock",
            Text = "Status: Ready",
        };
        rootPanel.Children.Add(testTextBlock);

        // ListBox: Name="testListBox" with three items
        var testListBox = new ListBox
        {
            Name = "testListBox",
            Width = 200,
            Height = 80,
        };
        testListBox.Items.Add("Item One");
        testListBox.Items.Add("Item Two");
        testListBox.Items.Add("Item Three");
        rootPanel.Children.Add(testListBox);

        // PasswordBox: Name="testPasswordBox" — used by redaction integration tests.
        // The Password property is redacted by RedactionFilter (PasswordBox-specific branch).
        var testPasswordBox = new PasswordBox
        {
            Name = "testPasswordBox",
            Width = 200,
            Height = 24,
            Password = "s3cr3t!",
        };
        rootPanel.Children.Add(testPasswordBox);

        // Button with a Command bound (M1-07 hasCommandBinding tests).
        var commandButton = new Button
        {
            Name = "testCommandButton",
            Content = "With Command",
            Width = 120,
            Height = 32,
            Command = ApplicationCommands.Copy,
        };
        rootPanel.Children.Add(commandButton);

        // Button without a Command (M1-07 hasCommandBinding tests — must be false).
        var noCommandButton = new Button
        {
            Name = "testNoCommandButton",
            Content = "No Command",
            Width = 120,
            Height = 32,
        };
        rootPanel.Children.Add(noCommandButton);

        // Button with a relay-style command that always allows execution (M2-01 tests).
        var executeCount = 0;
        var alwaysExecutableCommand = new RelayCommand(
            execute: _ => executeCount++,
            canExecute: _ => true);

        var executableCommandButton = new Button
        {
            Name = "testExecutableCommandButton",
            Content = "Executable Command",
            Width = 120,
            Height = 32,
            Command = alwaysExecutableCommand,
            CommandParameter = "test-param",
        };
        rootPanel.Children.Add(executableCommandButton);

        // Button with a relay command that never allows execution (M2-01 CannotExecuteCommand test).
        var neverExecutableCommand = new RelayCommand(
            execute: _ => { },
            canExecute: _ => false);

        var nonExecutableCommandButton = new Button
        {
            Name = "testNonExecutableCommandButton",
            Content = "Cannot Execute",
            Width = 120,
            Height = 32,
            Command = neverExecutableCommand,
        };
        rootPanel.Children.Add(nonExecutableCommandButton);

        // ── M2-05: wpf_click elements ────────────────────────────────────────────

        // Button with InvokePattern support and no Command — pure L1 click (M2-05).
        var clickableButton = new Button
        {
            Name = "testClickableButton",
            Content = "Click via Automation",
            Width = 120,
            Height = 32,
        };
        rootPanel.Children.Add(clickableButton);

        // Button with both InvokePattern and a Command — L1 succeeds but hint suggests L0 (M2-05).
        var commandAndClickButton = new Button
        {
            Name = "testClickableWithCommandButton",
            Content = "Click + Command",
            Width = 120,
            Height = 32,
            Command = new RelayCommand(execute: _ => { }, canExecute: _ => true),
        };
        rootPanel.Children.Add(commandAndClickButton);

        // TextBlock — does NOT support IInvokeProvider → PatternNotSupported (M2-05).
        var nonInvokableElement = new TextBlock
        {
            Name = "testNonInvokableElement",
            Text = "Non-invokable",
        };
        rootPanel.Children.Add(nonInvokableElement);

        // ── M2-04a: wpf_select_item elements ────────────────────────────────────

        // ComboBox: Name="testComboBox" — non-virtualized, used by SelectItem integration tests.
        var testComboBox = new ComboBox
        {
            Name = "testComboBox",
            Width = 200,
            Height = 24,
        };
        testComboBox.Items.Add("Apple");
        testComboBox.Items.Add("Banana");
        testComboBox.Items.Add("Cherry");
        rootPanel.Children.Add(testComboBox);

        // ── M2-03: wpf_set_check_state elements ─────────────────────────────────

        // CheckBox: Name="testCheckBox" — used by SetCheckState integration tests (M2-03).
        var testCheckBox = new CheckBox
        {
            Name = "testCheckBox",
            Content = "Test CheckBox",
            IsChecked = false,
            IsThreeState = true,
        };
        rootPanel.Children.Add(testCheckBox);

        // RadioButton: Name="testRadioButton" — used by SetCheckState integration tests (M2-03).
        var testRadioButton = new RadioButton
        {
            Name = "testRadioButton",
            Content = "Test RadioButton",
            IsChecked = false,
        };
        rootPanel.Children.Add(testRadioButton);

        // Bare ToggleButton: Name="testToggleButton" — used by SetCheckState rejection (M2-03) and Toggle tests (M2-06).
        var testToggleButton = new System.Windows.Controls.Primitives.ToggleButton
        {
            Name = "testToggleButton",
            Content = "Test ToggleButton",
            IsChecked = false,
        };
        rootPanel.Children.Add(testToggleButton);

        // ── M2-06: wpf_toggle elements ────────────────────────────────────────

        // Menu with a checkable MenuItem — used to verify IToggleProvider on MenuItem (M2-06).
        var checkableMenuItem = new MenuItem
        {
            Name = "testCheckableMenuItem",
            Header = "Checkable Item",
            IsCheckable = true,
            IsChecked = false,
        };
        var testMenu = new Menu
        {
            Name = "testMenu",
            Width = 200,
            Height = 24,
        };
        testMenu.Items.Add(checkableMenuItem);
        rootPanel.Children.Add(testMenu);

        // VirtualizingStackPanel-backed ListBox with 10 000 items (M1-06 / M2-04b).
        var testBigList = new ListBox
        {
            Name = "testBigList",
            Width = 200,
            Height = 80,
            ItemsSource = Enumerable.Range(0, 10_000).Select(i => $"Item {i}").ToList(),
        };
        VirtualizingStackPanel.SetIsVirtualizing(testBigList, true);
        VirtualizingStackPanel.SetVirtualizationMode(testBigList, VirtualizationMode.Recycling);
        rootPanel.Children.Add(testBigList);

        var window = new Window
        {
            Title = "SnoopWPF Integration Test Window",
            Name = "testMainWindow",
            Width = 400,
            Height = 300,
            Content = rootPanel,
        };

        return window;
    }
}
