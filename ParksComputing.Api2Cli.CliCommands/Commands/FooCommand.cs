using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

namespace ParksComputing.Api2Cli.CliCommands.Commands;

[Command("foostuff", "Do foo stuff.")]
public class FooCommand {
    public int Execute() {
    var console = ParksComputing.Api2Cli.Cli.Services.Utility.GetService<ParksComputing.Api2Cli.Cli.Services.IConsoleWriter>();
    console?.WriteLine("Foo command executed", category: "cli.foo", code: "foo.executed");
        return Result.Success;
    }
}
