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
        var uninstallResult = await xferKitApi.package.uninstallAsync(packageName);

        if (uninstallResult == null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error uninstalling package '{packageName}'.");
            return Result.Error;
        }

        if (uninstallResult.success) {
            Console.WriteLine($"{Constants.SuccessChar} {uninstallResult.message}");
        }
        else {
            Console.Error.WriteLine($"{Constants.ErrorChar} {uninstallResult.message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
