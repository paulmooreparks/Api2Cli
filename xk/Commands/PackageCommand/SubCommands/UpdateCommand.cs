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
        var packageInstallResult = await xferKitApi.Package.UpdateAsync(packageName);

        if (packageInstallResult == null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error updating package '{packageName}'.");
            return Result.Error;
        }

        if (packageInstallResult.Success) {
            Console.WriteLine($"{Constants.SuccessChar} {packageInstallResult.Message}");
        }
        else {
            Console.Error.WriteLine($"{Constants.ErrorChar} {packageInstallResult.Message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
