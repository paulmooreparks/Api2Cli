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
    void SetActiveWorkspace(string workspaceName);
}
