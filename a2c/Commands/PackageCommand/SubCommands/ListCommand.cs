using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("list", "List installed packages.", Parent = "package")]
internal class ListCommand(
    A2CApi Api2CliApi
    )
{
    public int Execute()
    {
        var plugins = Api2CliApi.Package.List;

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
