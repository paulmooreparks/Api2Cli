using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Microsoft.Extensions.DependencyInjection;

namespace ParksComputing.Api2Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void WorkspaceTree_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Pick the last added selection if present
        var item = e.AddedItems?.Count > 0 ? e.AddedItems[^1] as TreeItem : null;
        if (item is null && sender is TreeView tv)
        {
            item = tv.SelectedItem as TreeItem;
        }

        if (item is not null)
        {
            vm.OnTreeSelection(item);
        }
    }
}
