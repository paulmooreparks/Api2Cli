using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Workspace.Services;

public interface IWorkspaceService {
    BaseConfig BaseConfig { get; }
    IEnumerable<string> WorkspaceList { get; }
    WorkspaceDefinition ActiveWorkspace { get; }
    string CurrentWorkspaceName { get; }
    // Raised after the active workspace is updated to a non-empty, known workspace name.
    // Listeners (e.g., scripting orchestrator) can lazily initialize per-workspace processing here.
    event Action<string>? ActiveWorkspaceChanged;
    void SetActiveWorkspace(string workspaceName);
    void SaveConfig();
    /// <summary>
    /// Re-reads the workspace configuration file and refreshes the active workspace and loaded assemblies.
    /// </summary>
    void ReloadConfig();
}
