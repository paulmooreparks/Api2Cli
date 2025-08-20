using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.DataStore;
using ParksComputing.Api2Cli.Diagnostics;
using ParksComputing.Api2Cli.Http;
using ParksComputing.Api2Cli.Orchestration;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Gui;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddApi2CliWorkspaceServices();
        services.AddApi2CliHttpServices();
        services.AddApi2CliScriptingServices();
        services.AddApi2CliDiagnosticsServices("Api2Gui");
        services.AddApi2CliOrchestration();

        string databasePath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            Constants.Api2CliDirectoryName,
            Constants.StoreFileName
        );
        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(databasePath)))
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(databasePath)!);
        services.AddApi2CliDataStore(databasePath);

        Services = services.BuildServiceProvider();

        // Initialize scripting orchestrator (lazy internals remain)
        var orchestrator = Services.GetRequiredService<IWorkspaceScriptingOrchestrator>();
        orchestrator.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(Services)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
