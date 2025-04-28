using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;
using ParksComputing.XferKit.Workspace;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Cli.Commands.StoreCommand.SubCommands;

[Command("clear", "Clear all keys from the store", Parent = "store")]
internal class ClearCommand(
    IStoreService store
    ) 
{
    public int Execute() {
        store.Clear();
        Console.WriteLine("Store cleared.");
        return Result.Success;
    }
}
