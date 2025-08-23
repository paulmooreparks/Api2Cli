using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text;

using Cliffer;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Repl;

internal class WorkspaceReplContext : DefaultReplContext
{
    private readonly ICommandSplitter _commandSplitter;
    private readonly IWorkspaceService _workspaceService;

    public override string[] ExitCommands => ["exit"];

    public WorkspaceReplContext(
        System.CommandLine.Command currentCommand,
        System.CommandLine.RootCommand rootCommand,
        IWorkspaceService workspaceService,
        ICommandSplitter commandSplitter
        ) : base(currentCommand)
    {
        _workspaceService = workspaceService;
        _commandSplitter = commandSplitter;
    }

    public override string EntryMessage {
        get {
            if (_workspaceService.BaseConfig.Properties.TryGetValue("hideReplMessages", out var value) && value is bool hideReplMessages && hideReplMessages == true) {
                return string.Empty;
            }

            return base.EntryMessage;
        }
    }

    override public string GetPrompt(Command command, InvocationContext context) {
        return $"{_workspaceService.CurrentWorkspaceName}> ";
    }

    public override async Task<int> RunAsync(Command workspaceCommand, string[] args) {
        // Prevent recursive invocation of the same command
        if (args.Length > 0 && string.Equals(args[0], workspaceCommand.Name, StringComparison.OrdinalIgnoreCase)) {
            var console = Utility.GetService<IConsoleWriter>();
            console?.WriteLine($"Already in '{workspaceCommand.Name}' context.", category: "cli.repl", code: "context.duplicate", ctx: new Dictionary<string, object?> { ["context"] = workspaceCommand.Name });
            return Result.Success;
        }

        return await base.RunAsync(workspaceCommand, args);
    }

    public override string[] SplitCommandLine(string input) {
        return _commandSplitter.Split(input).ToArray();
    }
}
