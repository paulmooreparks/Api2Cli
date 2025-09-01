using System;
using Cliffer;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("delete", "Delete a key from the store", Parent = "store")]
[Argument(typeof(string), "key", "The key to delete")]
internal class DeleteCommand(
    IStoreService store,
    IConsoleWriter console
    )
{
    public int Execute(
        string key
        )
    {
        if (store.Remove(key)) {
            console.WriteLine($"Deleted key '{key}'.", category: "cli.store.delete", code: "store.delete.ok");
        }
        else {
            console.WriteError($"{Constants.ErrorChar} Key '{key}' not found.", category: "cli.store.delete", code: "store.delete.missing");
            return Result.Error;
        }

        return Result.Success;
    }
}
