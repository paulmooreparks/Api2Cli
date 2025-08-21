using System.CommandLine;
using System.CommandLine.Invocation;
using System;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.Admin;

[Command("reload", "Reload configuration in-process.")]
internal class ReloadCommand(
    IWorkspaceService workspaceService
) {
    public int Execute(
        Command command,
        InvocationContext context)
    {
        try {
            workspaceService.ReloadConfig();
            Console.WriteLine("Configuration reloaded.");

            return Result.Success;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Reload failed: {ex.Message}");
            return Result.Error;
        }
    }
}
