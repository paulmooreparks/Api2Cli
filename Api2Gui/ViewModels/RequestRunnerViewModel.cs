using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Cli.Commands;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Gui;

public class RequestRunnerViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceService _ws;
    private readonly IWorkspaceScriptingOrchestrator _orchestrator;

    public string WorkspaceName { get; }
    public string RequestName { get; }

    public string? BaseUrl { get; set; }
    public List<string> Headers { get; } = new();
    public List<string> Parameters { get; } = new();
    public string? Payload { get; set; }

    private string? _output;
    public string? Output
    {
        get => _output;
        private set { if (_output != value) { _output = value; OnPropertyChanged(nameof(Output)); } }
    }

    public ICommand RunCommand { get; }

    public string HeadersText
    {
        get => string.Join("\n", Headers);
        set {
            Headers.Clear();
            if (!string.IsNullOrWhiteSpace(value))
                Headers.AddRange(value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        }
    }

    public string ParametersText
    {
        get => string.Join("\n", Parameters);
        set {
            Parameters.Clear();
            if (!string.IsNullOrWhiteSpace(value))
                Parameters.AddRange(value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        }
    }

    public RequestRunnerViewModel(string workspaceName, string requestName, IServiceProvider services)
    {
        WorkspaceName = workspaceName;
        RequestName = requestName;
        _services = services;
        _ws = services.GetRequiredService<IWorkspaceService>();
    _orchestrator = services.GetRequiredService<IWorkspaceScriptingOrchestrator>();
    var wsDef = _ws.BaseConfig.Workspaces[workspaceName];
        BaseUrl = wsDef.BaseUrl;
        RunCommand = new RelayCommand(_ => Run());
    }

    public void Run()
    {
        try
        {
            _ws.SetActiveWorkspace(WorkspaceName);
            _orchestrator.ActivateWorkspace(WorkspaceName);
            var send = new SendCommand(
                _services.GetRequiredService<ParksComputing.Api2Cli.Http.Services.IHttpService>(),
                _services.GetRequiredService<ParksComputing.Api2Cli.Api.A2CApi>(),
                _ws,
                _services.GetRequiredService<ParksComputing.Api2Cli.Scripting.Services.IApi2CliScriptEngineFactory>(),
                _services.GetRequiredService<IWorkspaceScriptingOrchestrator>(),
                _services.GetRequiredService<ParksComputing.Api2Cli.Scripting.Services.IPropertyResolver>(),
                _services.GetRequiredService<ParksComputing.Api2Cli.Workspace.Services.ISettingsService>()
            );

            var result = send.DoCommand(
                WorkspaceName,
                RequestName,
                BaseUrl,
                Parameters,
                Payload,
                Headers,
                null,
                null,
                null
            );

            Output = send.CommandResult?.ToString();
        }
        catch (Exception ex)
        {
            Output = ex.Message;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
