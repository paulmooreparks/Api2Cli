using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("list", "List installed packages.", Parent = "package")]
internal class ListCommand(
    A2CApi Api2CliApi,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public int Execute() {
        var plugins = Api2CliApi.Package.List;

        if (plugins.Any()) {
            _console.WriteLineKey("package.list.header", category: "cli.package", code: "package.list.header");
            foreach (var plugin in plugins) {
                _console.WriteLineKey("package.list.item", category: "cli.package", code: "package.list.item", ctx: new Dictionary<string, object?> { ["plugin"] = plugin, ["value"] = plugin });
            }
        }
        else {
            _console.WriteLineKey("package.list.empty", category: "cli.package", code: "package.list.empty");
        }

        return Result.Success;
    }
}
