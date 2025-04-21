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
    private readonly System.CommandLine.RootCommand _rootCommand;
    private readonly ICommandSplitter _commandSplitter;
    private readonly IWorkspaceService _workspaceService;

    public override string[] GetExitCommands() => ["exit"];
    // public override string[] GetPopCommands() => ["/"];
    public override string[] GetHelpCommands() => ["-?", "-h", "--help", "?", "help", "/?"];

    public WorkspaceReplContext(
        System.CommandLine.RootCommand rootCommand,
        IWorkspaceService workspaceService,
        ICommandSplitter commandSplitter
        )
    {
        _rootCommand = rootCommand;
        _workspaceService = workspaceService;
        _commandSplitter = commandSplitter;
    }

    public override string GetEntryMessage() {
        if (_workspaceService.BaseConfig.Properties.TryGetValue("hideReplMessages", out var value) && value is bool hideReplMessages && hideReplMessages == true) {
            return string.Empty;
        }

        return base.GetEntryMessage();
    }

    override public string GetPrompt(Command command, InvocationContext context) {
        return $"{_workspaceService.CurrentWorkspaceName}> ";
    }

    public override async Task<int> RunAsync(Command workspaceCommand, string[] args) {
        if (args.Length == 0) {
            return Result.Success;
        }

        string firstArg = args[0];

        if (string.Equals(firstArg, workspaceCommand.Name, StringComparison.OrdinalIgnoreCase)) {
            Console.Error.WriteLine($"Already in '{workspaceCommand.Name}' context.");
            return Result.Success;
        }

        // Handle /command → top-level
        if (firstArg.StartsWith('/')) {
            string[] rewrittenArgs = args.ToArray();
            rewrittenArgs[0] = firstArg.TrimStart('/');
            var rootParser = new Parser(_rootCommand);  // rootCommand must be captured via constructor
            var parseResult = rootParser.Parse(rewrittenArgs);

            return await parseResult.InvokeAsync();
        }

        // Handle ../command → one level up
        if (firstArg.StartsWith("..")) {
            string[] rewrittenArgs = args.ToArray();
            rewrittenArgs[0] = firstArg.TrimStart('.');
            var parentParser = new Parser(_rootCommand); // In absence of true hierarchy, fallback to root
            var parseResult = parentParser.Parse(rewrittenArgs);

            return await parseResult.InvokeAsync();
        }

        // Try workspace parser first
        var workspaceParser = new Parser(workspaceCommand);
        var parseResultWorkspace = workspaceParser.Parse(args);

        if (parseResultWorkspace.Errors.Count == 0) {
            if (args[0].StartsWith('-')) {
                return await base.RunAsync(workspaceCommand, args);
            }
            else if (parseResultWorkspace.CommandResult.Command != workspaceCommand) {
                return await parseResultWorkspace.InvokeAsync();
            }
        }

        // Fall back to root parser if not matched in workspace
        var rootParserFallback = new Parser(_rootCommand);
        var parseResultRoot = rootParserFallback.Parse(args);

        if (parseResultRoot.Errors.Count == 0 && parseResultRoot.CommandResult.Command != _rootCommand) {
            return await parseResultRoot.InvokeAsync();
        }

        // Show error if unmatched
        Console.Error.WriteLine($"{Constants.ErrorChar} {parseResultWorkspace.Errors.FirstOrDefault()?.Message ?? "Unknown command"}");
        return Result.ErrorInvalidArgument;
    }

    public override string[] SplitCommandLine(string input) {
        return _commandSplitter.Split(input).ToArray();
    }
}
