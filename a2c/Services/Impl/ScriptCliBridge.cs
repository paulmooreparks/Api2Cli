using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Cli.Services.Impl;
public class ScriptCliBridge : IScriptCliBridge {
    public System.CommandLine.RootCommand? RootCommand { get; set; } = default;

    public ScriptCliBridge() {
    }

    [ScriptMember("runCommand")]
    public int RunCommand(string commandName, params object?[] args) {
    // Emit via console writer if available; category cli.scriptBridge
    var console = ParksComputing.Api2Cli.Cli.Services.Utility.GetService<ParksComputing.Api2Cli.Cli.Services.IConsoleWriter>();
    console?.WriteLine(commandName, category: "cli.scriptBridge", code: "command.invoke", ctx: new Dictionary<string, object?> { ["command"] = commandName });
        return Result.Success;
    }
}
