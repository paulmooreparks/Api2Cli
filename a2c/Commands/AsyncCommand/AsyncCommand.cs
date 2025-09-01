using Cliffer;
using System.CommandLine;
using System.CommandLine.Invocation;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Runtime.Services.Jobs;

namespace ParksComputing.Api2Cli.Cli.Commands.Async;

[Command("async", "Asynchronous (queued) operations")]
internal class AsyncCommand(
    IServiceProvider serviceProvider,
    IWorkspaceService workspaceService
    )
{
    // Root async command currently just provides help; subcommands implement behavior.
    public async Task<int> Execute(Command command, InvocationContext context) {
        var replContext = new SubcommandReplContext(
            command,
            workspaceService,
            new CommandSplitter()
            );

        var result = await command.Repl(
            serviceProvider,
            context,
            replContext
            );

        return result;
    }
}

[Command("run", "Queue a script for asynchronous execution", Parent = "async")]
[Argument(typeof(string), "workspace", "Workspace name (optional)", Arity = Cliffer.ArgumentArity.ZeroOrOne)]
[Argument(typeof(string), "script", "Script name", Arity = Cliffer.ArgumentArity.ExactlyOne)]
internal class AsyncRunCommand {
    private readonly IWorkspaceService _workspaceService;
    private readonly IApi2CliScriptEngineFactory _engineFactory;
    private readonly IConsoleWriter _console;
    private readonly IJobManager _jobs;

    public AsyncRunCommand(
        IWorkspaceService workspaceService,
        IApi2CliScriptEngineFactory engineFactory,
        IConsoleWriter consoleWriter,
        IJobManager jobManager
        )
    {
        _workspaceService = workspaceService;
        _engineFactory = engineFactory;
        _console = consoleWriter;
        _jobs = jobManager;
    }

    public int Execute(
        string script,
        string? workspace,
        InvocationContext context
        )
    {
        var engine = _engineFactory.GetEngine(ParksComputing.Api2Cli.Scripting.Services.ScriptEngineKinds.JavaScript);
        var handler = new RunWsScriptCommand(_workspaceService, _engineFactory, engine, _console);
        var job = _jobs.Enqueue(new JobRequest("script", string.IsNullOrEmpty(workspace)? script : workspace+"."+script, async ct => {
            return await Task.Run<object?>(() => {
                handler.DoCommand(context, script, workspace, null, null);
                return handler.CommandResult;
            }, ct);
        }));
        _console.WriteLineKey("jobs.enqueued", "cli.jobs", code: "jobs.enqueued", ctx: new Dictionary<string, object?> { ["id"] = job.Id, ["name"] = job.Name, ["kind"] = job.Kind });
        return Result.Success;
    }
}
