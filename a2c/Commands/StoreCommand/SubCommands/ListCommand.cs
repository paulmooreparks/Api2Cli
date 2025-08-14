using Cliffer;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("list", "List all keys and their values", Parent = "store")]
internal class ListCommand(
    IStoreService store
    ) 
{
    public int Execute() {
        if (store.Count == 0) {
            Console.Error.WriteLine($"{Constants.WarningChar} Store is empty.");
        }
        else {
            foreach (var kvp in store)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }
        }

        return Result.Success;
    }
}
