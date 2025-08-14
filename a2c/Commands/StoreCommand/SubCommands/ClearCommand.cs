using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using ParksComputing.Api2Cli.Cli.Utilities;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("clear", "Clear all keys from the store", Parent = "store")]
internal class ClearCommand(
    IStoreService store
    ) 
{
    public int Execute() {
        if (!ConsolePrompts.Confirm("Are you sure you want to clear all keys from the store?")) {
            Console.WriteLine("Operation cancelled.");
            return Result.Success;
        }

        store.Clear();
        Console.WriteLine("Store cleared.");
        return Result.Success;
    }
}
