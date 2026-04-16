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

    public SampleViewModel()
    {
        Items = BuildItems(50);
        TreeItems = BuildTreeItems();
        StatusOptions = new List<string> { "Ready", "Running", "Error", "Stopped" };
        RefreshCommand = new RelayCommand(_ => Refresh());
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string SearchText
    {
        get => searchText;
        set => SetField(ref searchText, value);
    }

    public bool IsAlertActive
    {
        get => isAlertActive;
        set => SetField(ref isAlertActive, value);
    }

    public double SliderValue
    {
        get => sliderValue;
        set => SetField(ref sliderValue, value);
    }

    public string SelectedStatus
    {
        get => selectedStatus;
        set => SetField(ref selectedStatus, value);
    }

    public string SelectedItemLabel
    {
        get => selectedItemLabel;
        set => SetField(ref selectedItemLabel, value);
    }

    public ObservableCollection<SampleItem> Items { get; }

    public List<TreeNode> TreeItems { get; }

    public List<string> StatusOptions { get; }

    public ICommand RefreshCommand { get; }

    public void Refresh()
    {
        Name = $"Refreshed at tick {System.Environment.TickCount64}";
    }

    public void ExecuteSearch()
    {
        SelectedItemLabel = $"Searched: {SearchText}";
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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

/// <summary>A single item in the non-virtualized ListBox.</summary>
public sealed record SampleItem(int Index, string Label);

/// <summary>A node in the sample TreeView hierarchy.</summary>
public sealed class TreeNode
{
    public TreeNode(string label, params TreeNode[] children)
    {
        Label = label;
        Children = new List<TreeNode>(children);
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

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);
}
