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
        var packageInstallResult = await xferKitApi.Package.InstallAsync(packageName);

        if (packageInstallResult == null)
        {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error installing package '{packageName}'.");
            return Result.Error;
        }

        if (packageInstallResult.Success)
        {
            Console.WriteLine($"{Constants.SuccessChar} Installed {packageInstallResult.PackageName} {packageInstallResult.Version} to {packageInstallResult.Path}");
        }
        else
        {
            Console.Error.WriteLine($"{Constants.ErrorChar} Failed to install package '{packageName}': {packageInstallResult.Message}");
            return Result.Error;
        }

        return Result.Success;
    }
}
