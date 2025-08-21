using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Gui;

public class ScriptRunnerViewModel : System.ComponentModel.INotifyPropertyChanged {
    private readonly IWorkspaceService _ws;
    private readonly IApi2CliScriptEngineFactory _engineFactory;
    private readonly IWorkspaceScriptingOrchestrator _orchestrator;

    public string WorkspaceName { get; }
    public string ScriptName { get; }
    public List<ArgumentVm> Args { get; } = new();
    private string? _output;
    public string? Output {
        get => _output;
        private set {
            if (_output != value) {
                _output = value;
                OnPropertyChanged(nameof(Output));
            }
        }
    }

    public ICommand RunCommand { get; }

    public ScriptRunnerViewModel(string workspaceName, string scriptName, IServiceProvider services) {
        WorkspaceName = workspaceName;
        ScriptName = scriptName;
        _ws = services.GetRequiredService<IWorkspaceService>();
        _engineFactory = services.GetRequiredService<IApi2CliScriptEngineFactory>();
        _orchestrator = services.GetRequiredService<IWorkspaceScriptingOrchestrator>();

        var def = _ws.BaseConfig.Workspaces[workspaceName].Scripts[scriptName];
        foreach (var kv in def.Arguments) {
            Args.Add(new ArgumentVm { Name = kv.Key, Description = kv.Value.Description, Type = kv.Value.Type ?? "string", IsRequired = kv.Value.IsRequired });
        }

        RunCommand = new RelayCommand(_ => Run());
    }

    public void Run() {
        try {
            _ws.SetActiveWorkspace(WorkspaceName);
            _orchestrator.ActivateWorkspace(WorkspaceName);
            var engine = _engineFactory.GetEngine("javascript");

            var argVals = Args.Select(a => (object?) a.Value).ToArray();
            var result = engine.Invoke($"a2c.workspaces['{WorkspaceName}']['{ScriptName}']", argVals);
            Output = result?.ToString();
        }
        catch (Exception ex) {
            Output = ex.Message;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class ArgumentVm {
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "string";
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}

public class RelayCommand : ICommand {
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
