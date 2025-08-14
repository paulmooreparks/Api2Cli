using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("install", "Install a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to install")]
internal class InstallCommand(
    A2CApi Api2CliApi
    )
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        )
    {
        var packageInstallResult = await Api2CliApi.Package.InstallAsync(packageName);

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
