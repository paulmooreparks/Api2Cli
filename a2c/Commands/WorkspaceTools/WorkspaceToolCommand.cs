using System.CommandLine;
using System.CommandLine.Invocation;

using Cliffer;

using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("workspace", "Workspace utilities (create/import/etc.)")]
internal class WorkspaceToolCommand(
    IServiceProvider serviceProvider,
    IWorkspaceService workspaceService
) {
    public async Task<int> Execute(Command command, InvocationContext context) {
        var replContext = new SubcommandReplContext(
            command,
            workspaceService,
            new CommandSplitter()
        );

        var result = await command.Repl(
            serviceProvider,
            context,
            replContext
        );

        return result;
    }
}
