using System.CommandLine;
using System.CommandLine.Invocation;

using Cliffer;

using ParksComputing.XferKit.Cli.Services.Impl;
using ParksComputing.XferKit.Workspace;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand;

[Command("package", "Install, update, list, and remove packages.")]
internal class PackageCommand(
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
