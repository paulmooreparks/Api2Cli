using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand.SubCommands;

[Command("update", "Update a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to update")]
internal class UpdateCommand(
    XferKitApi xferKitApi
    ) 
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        ) 
    {
        var packageInstallResult = await xferKitApi.package.updateAsync(packageName);

        if (packageInstallResult == null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error updating package '{packageName}'.");
            return Result.Error;
        }

        if (packageInstallResult.success) {
            Console.WriteLine($"{Constants.SuccessChar} {packageInstallResult.message}");
        }
        else {
            Console.Error.WriteLine($"{Constants.ErrorChar} {packageInstallResult.message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
