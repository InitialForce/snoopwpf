namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
