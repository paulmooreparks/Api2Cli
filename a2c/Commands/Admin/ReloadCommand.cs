using System.CommandLine;
using System.CommandLine.Invocation;
using System;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.Admin;

[Command("reload", "Reload configuration in-process.")]
internal class ReloadCommand(
    IWorkspaceService workspaceService,
    ParksComputing.Api2Cli.Orchestration.Services.IWorkspaceScriptingOrchestrator orchestrator,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter console
) {
    public int Execute(
        Command command,
        InvocationContext context)
    {
        try {
            var prev = workspaceService.CurrentWorkspaceName;
            // Reload config first so BaseConfig reflects new state
            workspaceService.ReloadConfig();
            // Reset scripting so init scripts re-run just like a fresh process
            orchestrator.ResetForReload();
            orchestrator.Initialize();
            // Re-activate previously active workspace (will trigger init scripts again)
            if (!string.IsNullOrWhiteSpace(prev)) {
                orchestrator.ActivateWorkspace(prev);
            } else if (!string.IsNullOrWhiteSpace(workspaceService.CurrentWorkspaceName)) {
                orchestrator.ActivateWorkspace(workspaceService.CurrentWorkspaceName);
            }
            console.WriteLine("Configuration reloaded (scripting reset).", category: "cli.reload", code: "reload.success");

            return Result.Success;
        }
        catch (Exception ex) {
            console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Reload failed: {ex.Message}", category: "cli.reload", code: "reload.failure", ex: ex);
            return Result.Error;
        }
    }
}
