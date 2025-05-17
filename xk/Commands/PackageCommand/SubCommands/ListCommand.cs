using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand.SubCommands;

[Command("list", "List installed packages.", Parent = "package")]
internal class ListCommand(
    XferKitApi xferKitApi
    )
{
    public int Execute()
    {
        var plugins = xferKitApi.Package.List;

        if (plugins.Count() > 0) {
            Console.WriteLine("Installed Plugins:");
            foreach (var plugin in plugins) {
                Console.WriteLine($"  - {plugin}");
            }
        }
        else {
            Console.WriteLine($"{Constants.WarningChar} No plugins installed.");
        }

        return Result.Success;
    }
}
