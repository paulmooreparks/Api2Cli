using System;
using Cliffer;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("set", "Set the value for a key", Parent = "store")]
[Argument(typeof(string), "key", "The key to set")]
[Argument(typeof(string), "value", "The value to set")]
internal class SetCommand(
    IStoreService store,
    IConsoleWriter console
    )
{
    public int Execute(
        string key,
        string value
        )
    {
        store[key] = value;
        console.WriteLine($"Set key '{key}' to '{value}'.", category: "cli.store.set", code: "store.set.ok");
        return Result.Success;
    }
}
