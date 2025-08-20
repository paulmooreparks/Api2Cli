using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Gui;

public class WorkspaceDetailViewModel
{
    public string Name { get; }
    public string? BaseUrl { get; }

    public WorkspaceDetailViewModel(string name, IWorkspaceService ws)
    {
        Name = name;
        var def = ws.BaseConfig.Workspaces[name];
        BaseUrl = def.BaseUrl;
    }
}
