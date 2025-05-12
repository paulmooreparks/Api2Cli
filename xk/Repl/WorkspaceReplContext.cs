using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text;

using Cliffer;
using ParksComputing.XferKit.Cli.Services;
using ParksComputing.XferKit.Workspace;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Cli.Repl;

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
        return await base.RunAsync(workspaceCommand, args);    
    }

    public override string[] SplitCommandLine(string input) {
        return _commandSplitter.Split(input).ToArray();
    }
}
