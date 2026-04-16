namespace SnoopWPF.SampleApp;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

/// <summary>
/// ViewModel that provides sample data for testing Snoop inspection features:
/// - Binding errors (NonExistentProperty)
/// - Bound TextBox, ListBox, TreeView
/// - Bool/numeric properties for property inspection
/// - ICommand for behavior testing
/// </summary>
public sealed class SampleViewModel : INotifyPropertyChanged
{
    private string name = "Hello, SnoopWPF!";
    private string searchText = string.Empty;
    private bool isAlertActive;
    private double sliderValue = 42.0;
    private string selectedStatus = "Ready";
    private string selectedItemLabel = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="SampleViewModel"/> class.</summary>
    public SampleViewModel()
    {
        this.Items = BuildItems(50);
        this.TreeItems = BuildTreeItems();
        this.StatusOptions = new List<string> { "Ready", "Running", "Error", "Stopped" };
        this.RefreshCommand = new RelayCommand(_ => this.Refresh());
    }

    /// <summary>Gets or sets the display name.</summary>
    public string Name
    {
        get => this.name;
        set => this.SetField(ref this.name, value);
    }

    /// <summary>Gets or sets the search text.</summary>
    public string SearchText
    {
        get => this.searchText;
        set => this.SetField(ref this.searchText, value);
    }

    /// <summary>Gets or sets a value indicating whether the alert is active.</summary>
    public bool IsAlertActive
    {
        get => this.isAlertActive;
        set => this.SetField(ref this.isAlertActive, value);
    }

    /// <summary>Gets or sets the slider value.</summary>
    public double SliderValue
    {
        get => this.sliderValue;
        set => this.SetField(ref this.sliderValue, value);
    }

    /// <summary>Gets or sets the selected status.</summary>
    public string SelectedStatus
    {
        get => this.selectedStatus;
        set => this.SetField(ref this.selectedStatus, value);
    }

    /// <summary>Gets or sets the selected item label.</summary>
    public string SelectedItemLabel
    {
        get => this.selectedItemLabel;
        set => this.SetField(ref this.selectedItemLabel, value);
    }

    public ObservableCollection<SampleItem> Items { get; }

    public List<TreeNode> TreeItems { get; }

    public List<string> StatusOptions { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>Refreshes the Name property with the current tick count.</summary>
    public void Refresh()
    {
        this.Name = $"Refreshed at tick {System.Environment.TickCount64}";
    }

    /// <summary>Executes the search and updates SelectedItemLabel.</summary>
    public void ExecuteSearch()
    {
        this.SelectedItemLabel = $"Searched: {this.SearchText}";
    }

    private static ObservableCollection<SampleItem> BuildItems(int count)
    {
        var items = new ObservableCollection<SampleItem>();
        for (int i = 0; i < count; i++)
        {
            items.Add(new SampleItem(i, $"Item {i:D3}"));
        }

        return items;
    }

    private static List<TreeNode> BuildTreeItems()
    {
        return new List<TreeNode>
        {
            new TreeNode("Root A",
                new TreeNode("Child A1",
                    new TreeNode("Leaf A1a"),
                    new TreeNode("Leaf A1b")),
                new TreeNode("Child A2")),
            new TreeNode("Root B",
                new TreeNode("Child B1"),
                new TreeNode("Child B2",
                    new TreeNode("Leaf B2a"))),
        };
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

/// <summary>A single item in the non-virtualized ListBox.</summary>
public sealed record SampleItem(int Index, string Label);

/// <summary>A node in the sample TreeView hierarchy.</summary>
public sealed class TreeNode
{
    /// <summary>Initializes a new instance of the <see cref="TreeNode"/> class.</summary>
    public TreeNode(string label, params TreeNode[] children)
    {
        this.Label = label;
        this.Children = new List<TreeNode>(children);
    }

    public string Label { get; }

    public List<TreeNode> Children { get; }
}

/// <summary>Minimal ICommand implementation used by the sample ViewModel.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly System.Action<object?> execute;
    private readonly System.Func<object?, bool>? canExecute;

    public RelayCommand(System.Action<object?> execute, System.Func<object?, bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event System.EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => this.canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => this.execute(parameter);
}
