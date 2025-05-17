using Cliffer;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace;

namespace ParksComputing.XferKit.Cli.Commands.PackageCommand.SubCommands;

[Command("search", "Search for packages.", Parent = "package")]
[Argument(typeof(string), "packageName", "Name or partial name of the package to search for")]
internal class SearchCommand(
    XferKitApi xferKitApi
    ) 
{
    public async Task<int> Execute(
        [ArgumentParam("packageName")] string packageName
        ) 
    {
        var searchResult = await xferKitApi.Package.SearchAsync(packageName);

        if (searchResult == null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Unexpected error searching for package '{packageName}'.");
            return Result.Error;
        }

        if (searchResult.Success == false) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Error searching for packages: {searchResult.Message}");
            return Result.Error;
        }

        if (searchResult.List is null || searchResult.List.Count() == 0) {
            Console.Error.WriteLine($"{Constants.ErrorChar} No results found for search term '{packageName}'.");
            return Result.Error;
        }

        Console.WriteLine($"Search results for search term '{packageName}':");

        foreach (var package in searchResult.List) {
            Console.WriteLine($"  - {package}");
        }

        return Result.Success;
    }
}
