using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand.SubCommands;

[Command("install", "Install a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to install")]
internal class InstallCommand(
    XferKitApi xferKitApi
    ) 
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        )
    {
        var packageInstallResult = await xferKitApi.package.installAsync(packageName);

        if (packageInstallResult == null)
        {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error installing package '{packageName}'.");
            return Result.Error;
        }

        if (packageInstallResult.success)
        {
            Console.WriteLine($"{Constants.SuccessChar} Installed {packageInstallResult.packageName} {packageInstallResult.version} to {packageInstallResult.path}");
        }
        else
        {
            Console.Error.WriteLine($"{Constants.ErrorChar} Failed to install package '{packageName}': {packageInstallResult.message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
