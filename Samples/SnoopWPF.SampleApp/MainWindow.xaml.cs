namespace SnoopWPF.SampleApp;

using System.Windows;

/// <summary>
/// Code-behind for the main sample window.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SampleViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        viewModel = new SampleViewModel();
        DataContext = viewModel;
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        viewModel.Refresh();
    }

    private void OnToggleAlertClicked(object sender, RoutedEventArgs e)
    {
        viewModel.IsAlertActive = !viewModel.IsAlertActive;
    }

    private void OnSearchClicked(object sender, RoutedEventArgs e)
    {
        viewModel.ExecuteSearch();
    }
}
