using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Gui;

public class MainWindowViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly ServiceProvider _services;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWorkspaceScriptingOrchestrator _orchestrator;

    public ObservableCollection<TreeItem> WorkspaceItems { get; } = new();
    private object? _detail;
    public object? Detail
    {
        get => _detail;
        set
        {
            if (!object.ReferenceEquals(_detail, value))
            {
                _detail = value;
                OnPropertyChanged(nameof(Detail));
            }
        }
    }

    public MainWindowViewModel(ServiceProvider services)
    {
        _services = services;
        _workspaceService = services.GetRequiredService<IWorkspaceService>();
        _orchestrator = services.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        LoadTree();
    }

    private void LoadTree()
    {
        WorkspaceItems.Clear();
        var bc = _workspaceService.BaseConfig;
        foreach (var ws in bc.Workspaces.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var wsItem = new TreeItem(TreeItemKind.Workspace, ws.Key);
            // Scripts
            foreach (var s in ws.Value.Scripts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                wsItem.Children.Add(new TreeItem(TreeItemKind.Script, s.Key) { Tag = ws.Key });
            }
            // Requests
            foreach (var r in ws.Value.Requests.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                wsItem.Children.Add(new TreeItem(TreeItemKind.Request, r.Key) { Tag = ws.Key });
            }
            WorkspaceItems.Add(wsItem);
        }
    }

    public void OnTreeSelection(TreeItem item)
    {
        switch (item.Kind)
        {
            case TreeItemKind.Workspace:
                _workspaceService.SetActiveWorkspace(item.Title);
                _orchestrator.ActivateWorkspace(item.Title);
                Detail = new WorkspaceDetailViewModel(item.Title, _workspaceService);
                break;
            case TreeItemKind.Script:
                Detail = new ScriptRunnerViewModel(item.Tag?.ToString()!, item.Title, _services);
                break;
            case TreeItemKind.Request:
                Detail = new RequestRunnerViewModel(item.Tag?.ToString()!, item.Title, _services);
                break;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
