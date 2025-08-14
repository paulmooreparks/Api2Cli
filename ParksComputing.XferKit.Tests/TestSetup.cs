using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Http;
using ParksComputing.Api2Cli.Scripting;
using ParksComputing.Api2Cli.Diagnostics;
// using ParksComputing.Api2Cli.Cli.Services;
// using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.DataStore;

namespace ParksComputing.Api2Cli.Tests;

public static class TestSetup
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddApi2CliWorkspaceServices();
        services.AddApi2CliHttpServices();
        services.AddApi2CliScriptingServices();
        services.AddApi2CliDiagnosticsServices("Api2Cli");
        // services.AddSingleton<ICommandSplitter, CommandSplitter>();
        // services.AddSingleton<IScriptCliBridge, ScriptCliBridge>();

        string databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.XferDirectoryName,
            Constants.StoreFileName
        );

        if (!Directory.Exists(Path.GetDirectoryName(databasePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }

        services.AddApi2CliDataStore(databasePath);

        return services.BuildServiceProvider();
    }
}
