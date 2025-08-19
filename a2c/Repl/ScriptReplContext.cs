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

namespace ParksComputing.Api2Cli.Cli.Repl;

internal class ScriptReplContext : DefaultReplContext
{
    private readonly IApi2CliScriptEngine _scriptEngine;
    private readonly IApi2CliScriptEngineFactory _scriptEngineFactory;
    private readonly ICommandSplitter _commandSplitter;
    private readonly IWorkspaceService _workspaceService;

    public ScriptReplContext(
        Command currentCommand,
        IApi2CliScriptEngineFactory scriptEngineFactory,
        ICommandSplitter commandSplitter,
        IWorkspaceService workspaceService
        ) : base( currentCommand )
    {
        _scriptEngineFactory = scriptEngineFactory;
    _scriptEngine = _scriptEngineFactory.GetEngine(ParksComputing.Api2Cli.Scripting.Services.ScriptEngineKinds.JavaScript);
        _commandSplitter = commandSplitter;
        _workspaceService = workspaceService;
    }

    public override string[] ExitCommands => ["exit"];
    public override string[] PopCommands => ["quit"];
    public override string[] HelpCommands => ["-?", "-h", "--help"];

    override public string GetPrompt(Command command, InvocationContext context)
    {
        return $"{command.Name}> ";
    }

    public override string EntryMessage {
        get {
            if (_workspaceService.BaseConfig.Properties.TryGetValue("hideReplMessages", out var value) && value is bool hideReplMessages && hideReplMessages == true) {
                return string.Empty;
            }

            return base.EntryMessage;
        }
    }

    public override void OnEntry()
    {
        base.OnEntry();
    }

    public override string[] SplitCommandLine(string input)
    {
        return _commandSplitter.Split(input).ToArray();
    }

    public override async Task<int> RunAsync(Command command, string[] args)
    {
        var helpCommands = HelpCommands;
        var isHelp = helpCommands.Contains(args[0]);

        if (args.Length > 0 && !isHelp)
        {
            var script = string.Join(' ', args);
            var result = _scriptEngine.EvaluateScript(script);

            if (result is Task taskResult)
            {
                await taskResult.ConfigureAwait(false);

                // Check if it's a Task<T> with a result
                var taskType = taskResult.GetType();
                if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var property = taskType.GetProperty("Result");
                    var taskResultValue = property?.GetValue(taskResult);
                    if (taskResultValue is not null)
                    {
                        Console.WriteLine(taskResultValue);
                    }
                }
            }
            else if (result is ValueTask valueTaskResult)
            {
                await valueTaskResult.ConfigureAwait(false);
            }
            else
            {
                if (result is not null && !result.Equals(Undefined.Value))
                {
                    Console.WriteLine(result);
                }
            }

            return Result.Success;
        }

        ClifferEventHandler.PreprocessArgs(args);
        return await base.RunAsync(command, args);
    }
}
