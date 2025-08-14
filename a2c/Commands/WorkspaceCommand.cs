using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Api;

using System.CommandLine;
using System.CommandLine.Invocation;

using System.Diagnostics;
using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Cli.Repl;

namespace ParksComputing.Api2Cli.Cli.Commands;

internal class WorkspaceCommand {
    private readonly System.CommandLine.RootCommand _rootCommand;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceService _workspaceService;

    public string WorkspaceName { get; }

    public WorkspaceCommand(
        string workspaceName,
        System.CommandLine.RootCommand rootCommand,
        IServiceProvider serviceProvider,
        IWorkspaceService workspaceService
        ) 
    { 
        WorkspaceName = workspaceName;
        _rootCommand = rootCommand;
        _serviceProvider = serviceProvider;
        _workspaceService = workspaceService;
    }

    public async Task<int> Execute(
        Command command,
        InvocationContext context,
        string? baseUrl
        ) 
    {
        var tmpWorkspace = _workspaceService.ActiveWorkspace;
        _workspaceService.SetActiveWorkspace(WorkspaceName);
        var workspace = _workspaceService.ActiveWorkspace;
        var tmpBaseUrl = workspace.BaseUrl;
        workspace.BaseUrl = baseUrl;

        var replContext = new WorkspaceReplContext(
            command,
            _rootCommand,
            _workspaceService,
            new CommandSplitter()
            );

        var result = await command.Repl(
            _serviceProvider,
            context,
            replContext
            );

        workspace.BaseUrl = tmpBaseUrl;
        _workspaceService.SetActiveWorkspace(tmpWorkspace?.Name ?? "/");
        return result;
    }
}
