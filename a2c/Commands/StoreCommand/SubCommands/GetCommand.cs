using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.StoreCommand.SubCommands;

[Command("get", "Retrieve the value for a given key", Parent = "store")]
[Argument(typeof(string), "key", "The key to retrieve")]
internal class GetCommand(
    IStoreService store,
    IConsoleWriter consoleWriter
    )
{
    private readonly IConsoleWriter _console = consoleWriter;
    public int Execute(
        string key
        )
    {
        if (store.TryGetValue(key, out var value)) {
            _console.WriteLine(value?.ToString() ?? string.Empty, category: "cli.store.get", code: "value.found", ctx: new Dictionary<string, object?> { ["key"] = key });
        }
        else {
            _console.WriteError($"{Constants.ErrorChar} Key '{key}' not found.", category: "cli.store.get", code: "key.notFound", ctx: new Dictionary<string, object?> { ["key"] = key });
            return Result.Error;
        }

        return Result.Success;
    }
}
