using Cliffer;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Workspace;

namespace ParksComputing.Api2Cli.Cli.Commands.PackageCommand.SubCommands;

[Command("search", "Search for packages.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name or partial name of the package to search for")]
internal class SearchCommand(
    A2CApi Api2CliApi,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        )
    {
        var searchResult = await Api2CliApi.Package.SearchAsync(packageName);

        if (searchResult == null) {
            _console.WriteError($"{Constants.ErrorChar} Unexpected error searching for package '{packageName}'.", category: "cli.package", code: "search.unexpected", ctx: new Dictionary<string, object?> { ["package"] = packageName });
            return Result.Error;
        }

        if (searchResult.Success == false) {
            _console.WriteError($"{Constants.ErrorChar} Error searching for packages: {searchResult.Message}", category: "cli.package", code: "search.failed", ctx: new Dictionary<string, object?> { ["package"] = packageName, ["message"] = searchResult.Message });
            return Result.Error;
        }

        if (searchResult.List is null || searchResult.List.Count() == 0) {
            _console.WriteError($"{Constants.ErrorChar} No results found for search term '{packageName}'.", category: "cli.package", code: "search.empty", ctx: new Dictionary<string, object?> { ["package"] = packageName });
            return Result.Error;
        }

        _console.WriteLine($"Search results for search term '{packageName}':", category: "cli.package", code: "search.header", ctx: new Dictionary<string, object?> { ["package"] = packageName });

        foreach (var package in searchResult.List) {
            _console.WriteLine(package, category: "cli.package", code: "search.item", ctx: new Dictionary<string, object?> { ["package"] = package });
        }

        return Result.Success;
    }
}
