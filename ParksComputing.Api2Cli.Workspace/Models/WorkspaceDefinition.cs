using ParksComputing.Xfer.Lang.Attributes;

namespace ParksComputing.Api2Cli.Workspace.Models;

public class WorkspaceDefinition : FolderDefinition {
    [XferProperty("extend")]
    public string? Extend { get; set; }
    [XferProperty("isHidden")]
    public bool IsHidden { get; set; } = false;
    [XferProperty("base")]
    public WorkspaceDefinition? Base { get; set; }
    // Optional: allows config.xfer to map an arbitrary workspace name to an external directory
    // e.g. workspaces { prod dir "workspaces/prod-v2" } decoupling logical name from folder name.
    [XferProperty("dir")]
    public string? Dir { get; set; }

    internal void Merge(WorkspaceDefinition? parentWorkspace) {
        if (parentWorkspace is null) {
            return;
        }

        Name ??= parentWorkspace.Name;
        Description ??= parentWorkspace.Description;
        Extend ??= parentWorkspace.Extend;
        Base ??= parentWorkspace.Base;

        BaseUrl ??= parentWorkspace.BaseUrl;
        // InitScript ??= parentWorkspace.InitScript;
        PreRequest ??= parentWorkspace.PreRequest;
        PostResponse ??= parentWorkspace.PostResponse;

        foreach (var kvp in parentWorkspace.Requests) {
            if (!Requests.ContainsKey(kvp.Key)) {
                Requests[kvp.Key] = kvp.Value;
            }
            else {
                Requests[kvp.Key].Merge(kvp.Value);
            }
        }

        foreach (var kvp in parentWorkspace.Scripts) {
            if (!Scripts.ContainsKey(kvp.Key)) {
                Scripts[kvp.Key] = kvp.Value;
            }
            else {
                Scripts[kvp.Key].Merge(kvp.Value);
            }
        }

        foreach (var kvp in parentWorkspace.Macros) {
            if (!Macros.ContainsKey(kvp.Key)) {
                Macros[kvp.Key] = kvp.Value;
            }
            else {
                Macros[kvp.Key].Merge(kvp.Value);
            }
        }

        foreach (var kvp in parentWorkspace.Properties) {
            if (!Properties.ContainsKey(kvp.Key)) {
                Properties[kvp.Key] = kvp.Value;
            }
        }
    }
}
