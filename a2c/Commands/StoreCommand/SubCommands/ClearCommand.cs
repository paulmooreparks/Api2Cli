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
    IStoreService store,
    ParksComputing.Api2Cli.Cli.Services.IConsoleWriter consoleWriter
    )
{
    private readonly ParksComputing.Api2Cli.Cli.Services.IConsoleWriter _console = consoleWriter;
    public int Execute() {
    if (!ConsolePrompts.Confirm(_console.Localize("store.clear.confirm"))) {
            _console.WriteLineKey("store.clear.cancelled", category: "cli.store.clear", code: "store.clear.cancelled");
            return Result.Success;
        }

        store.Clear();
        _console.WriteLineKey("store.clear.success", category: "cli.store.clear", code: "store.clear.success");
        return Result.Success;
    }
}
