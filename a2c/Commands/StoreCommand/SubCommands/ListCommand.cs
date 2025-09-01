using Cliffer;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("list", "List all keys and their values", Parent = "store")]
internal class ListCommand(
    IStoreService store,
    IConsoleWriter console
    )
{
    public int Execute() {
        if (store.Count == 0) {
            console.WriteError($"{Constants.WarningChar} Store is empty.", category: "cli.store.list", code: "store.list.empty");
        }
        else {
            foreach (var kvp in store) {
                console.WriteLine($"{kvp.Key}: {kvp.Value}", category: "cli.store.list", code: "store.list.item");
            }
        }

        return Result.Success;
    }
}
