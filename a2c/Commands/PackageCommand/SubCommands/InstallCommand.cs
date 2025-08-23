using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("install", "Install a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to install")]
internal class InstallCommand(
    A2CApi Api2CliApi,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        ) {
        var packageInstallResult = await Api2CliApi.Package.InstallAsync(packageName);

        if (packageInstallResult == null) {
            _console.WriteError($"{Constants.ErrorChar} Unexpected error installing package '{packageName}'.", category: "cli.package", code: "install.unexpected", ctx: new Dictionary<string, object?> { ["package"] = packageName });
            return Result.Error;
        }

        if (packageInstallResult.Success) {
            _console.WriteLine($"{Constants.SuccessChar} Installed {packageInstallResult.PackageName} {packageInstallResult.Version} to {packageInstallResult.Path}", category: "cli.package", code: "install.success", ctx: new Dictionary<string, object?> { ["package"] = packageInstallResult.PackageName, ["version"] = packageInstallResult.Version, ["path"] = packageInstallResult.Path });
        }
        else {
            _console.WriteError($"{Constants.ErrorChar} Failed to install package '{packageName}': {packageInstallResult.Message}", category: "cli.package", code: "install.failed", ctx: new Dictionary<string, object?> { ["package"] = packageName, ["message"] = packageInstallResult.Message });
            return Result.Error;
        }

        return Result.Success;
    }
}
