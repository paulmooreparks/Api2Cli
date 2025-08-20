using System.Collections.ObjectModel;

namespace ParksComputing.Api2Gui;

public enum TreeItemKind { Workspace, Script, Request }

public class TreeItem
{
    public TreeItemKind Kind { get; }
    public string Title { get; }
    public object? Tag { get; set; }
    public ObservableCollection<TreeItem> Children { get; } = new();

    public TreeItem(TreeItemKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }
}
