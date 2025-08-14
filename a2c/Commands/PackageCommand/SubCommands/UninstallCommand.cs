using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("uninstall", "Uninstall a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to uninstall")]
internal class UninstallCommand(
    Api2CliApi Api2CliApi
    ) 
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        ) 
    {
        var uninstallResult = await Api2CliApi.Package.UninstallAsync(packageName);

        if (uninstallResult == null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error uninstalling package '{packageName}'.");
            return Result.Error;
        }

        if (uninstallResult.Success) {
            Console.WriteLine($"{Constants.SuccessChar} {uninstallResult.Message}");
        }
        else {
            Console.Error.WriteLine($"{Constants.ErrorChar} {uninstallResult.Message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
