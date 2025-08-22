using System.CommandLine;
using System.CommandLine.Invocation;

using Cliffer;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli;

internal class SubcommandReplContext : Cliffer.DefaultReplContext {
    private readonly ICommandSplitter _commandSplitter;
    private readonly IWorkspaceService _workspaceService;

    public override string[] ExitCommands => ["exit"];
    public override string[] PopCommands => ["/"];

    public SubcommandReplContext(
        Command currentCommand,
        IWorkspaceService workspaceService,
        ICommandSplitter commandSplitter
        ) : base( currentCommand )
    {
        _workspaceService = workspaceService;
        _commandSplitter = commandSplitter;
    }

    override public string GetPrompt(Command command, InvocationContext context) {
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

    public override async Task<int> RunAsync(Command command, string[] args) {
        // Prevent recursive invocation of the same command
        if (string.Equals(args[0], command.Name, StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine($"Already in '{command.Name}' context.");
            return Result.Success;
        }

        return await base.RunAsync(command, args);
    }

    public override string[] SplitCommandLine(string input) {
        return _commandSplitter.Split(input).ToArray();
    }
}
