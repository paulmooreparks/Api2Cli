using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Api;

namespace ParksComputing.Api2Cli.Runtime.Services;

public interface IScriptRuntimeInitializer
{
    void Initialize(A2CApi a2c, IWorkspaceService workspaceService, IApi2CliScriptEngineFactory engineFactory);
}
