using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;
using System.CommandLine;
using System.CommandLine.Invocation;

using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using Microsoft.ClearScript;
using ParksComputing.Api2Cli.Cli.Repl;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("script", "Run JavaScript interactively")]
[Argument(typeof(IEnumerable<string>), "scriptBody", "Optional script text to execute.", Arity = Cliffer.ArgumentArity.ZeroOrMore)]
internal class ScriptCommand {
    private readonly IApi2CliScriptEngine _scriptEngine;
    private readonly IApi2CliScriptEngineFactory _scriptEngineFactory;
    private readonly IReplContext _replContext;

    public ScriptCommand(
        Command command,
        IApi2CliScriptEngineFactory scriptEngineFactory,
        ICommandSplitter splitter,
        IWorkspaceService workspaceService
        )
    {
        _scriptEngineFactory = scriptEngineFactory;
        _scriptEngine = _scriptEngineFactory.GetEngine("javascript");
        _replContext = new ScriptReplContext(command, _scriptEngineFactory, splitter, workspaceService);
    }

    public async Task<int> Execute(
        IEnumerable<string> scriptBody,
        Command command,
        IServiceProvider serviceProvider,
        InvocationContext context
        )
    {
        if (scriptBody is not null && scriptBody.Any()) {
            var script = string.Join(' ', scriptBody);

            try {
                var output = _scriptEngine.ExecuteCommand(script);

                if (output is not null && !output.Equals(Undefined.Value)) {
                    Console.WriteLine(output);
                }

                return Result.Success;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"{Workspace.Constants.ErrorChar} Error executing script: {ex.Message}");
            }

            return Result.Error;
        }

        return await command.Repl(serviceProvider, context, _replContext);
    }
}
