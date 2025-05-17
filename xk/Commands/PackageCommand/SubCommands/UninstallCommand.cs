using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand.SubCommands;

[Command("uninstall", "Uninstall a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to uninstall")]
internal class UninstallCommand(
    XferKitApi xferKitApi
    ) 
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        ) 
    {
        var uninstallResult = await xferKitApi.Package.UninstallAsync(packageName);

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
