namespace SnoopWPF.SampleApp;

using System.Windows;

/// <summary>
/// A hidden window kept open throughout the application lifetime.
/// Used to test:
/// - wpf_get_windows with includeHidden=true vs false
/// - Screenshot capture on elements that are hidden
/// </summary>
public partial class HiddenWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="HiddenWindow"/> class.</summary>
    public HiddenWindow()
    {
        this.InitializeComponent();
    }
}
