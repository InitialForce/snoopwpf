namespace SnoopWPF.SampleApp;

using System.Windows;

/// <summary>
/// Code-behind for the main sample window.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SampleViewModel viewModel;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        this.InitializeComponent();

        this.viewModel = new SampleViewModel();
        this.DataContext = this.viewModel;
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        this.viewModel.Refresh();
    }

    private void OnToggleAlertClicked(object sender, RoutedEventArgs e)
    {
        this.viewModel.IsAlertActive = !this.viewModel.IsAlertActive;
    }

    private void OnSearchClicked(object sender, RoutedEventArgs e)
    {
        this.viewModel.ExecuteSearch();
    }
}
