using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("uninstall", "Uninstall a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to uninstall")]
internal class UninstallCommand(
    A2CApi Api2CliApi,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        )
    {
        var uninstallResult = await Api2CliApi.Package.UninstallAsync(packageName);

        if (uninstallResult == null) {
            _console.WriteError($"{Constants.ErrorChar} Unexpected error uninstalling package '{packageName}'.", category: "cli.package", code: "uninstall.unexpected", ctx: new Dictionary<string, object?> { ["package"] = packageName });
            return Result.Error;
        }

        if (uninstallResult.Success) {
            _console.WriteLine($"{Constants.SuccessChar} {uninstallResult.Message}", category: "cli.package", code: "uninstall.success", ctx: new Dictionary<string, object?> { ["package"] = packageName });
        }
        else {
            _console.WriteError($"{Constants.ErrorChar} {uninstallResult.Message}", category: "cli.package", code: "uninstall.failed", ctx: new Dictionary<string, object?> { ["package"] = packageName, ["message"] = uninstallResult.Message });
            return Result.Error;
        }

        return Result.Success;
    }
}
