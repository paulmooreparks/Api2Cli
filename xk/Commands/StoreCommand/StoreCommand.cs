using System.CommandLine;
using System.CommandLine.Invocation;

using Cliffer;

using ParksComputing.XferKit.Cli.Services.Impl;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Cli.Commands.StoreCommand;

[Command("store", "Manage the key/value store")]
internal class StoreCommand(
    IServiceProvider serviceProvider,
    IWorkspaceService workspaceService
    ) 
{
    public async Task<int> Execute(
        Command command,
        InvocationContext context
        ) 
    {
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
