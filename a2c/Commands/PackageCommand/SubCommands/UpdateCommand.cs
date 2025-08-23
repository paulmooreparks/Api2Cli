using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("update", "Update a package.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name of the package to update")]
internal class UpdateCommand(
    A2CApi Api2CliApi,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        )
    {
        var packageInstallResult = await Api2CliApi.Package.UpdateAsync(packageName);

        if (packageInstallResult == null) {
            _console.WriteError($"{Constants.ErrorChar} Unexpected error updating package '{packageName}'.", category: "cli.package", code: "update.unexpected", ctx: new Dictionary<string, object?> { ["package"] = packageName });
            return Result.Error;
        }

        if (packageInstallResult.Success) {
            _console.WriteLine($"{Constants.SuccessChar} {packageInstallResult.Message}", category: "cli.package", code: "update.success", ctx: new Dictionary<string, object?> { ["package"] = packageName });
        }
        else {
            _console.WriteError($"{Constants.ErrorChar} {packageInstallResult.Message}", category: "cli.package", code: "update.failed", ctx: new Dictionary<string, object?> { ["package"] = packageName, ["message"] = packageInstallResult.Message });
            return Result.Error;
        }

        return Result.Success;
    }
}
