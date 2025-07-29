using Microsoft.Extensions.DependencyInjection;
using ParksComputing.XferKit.Workspace;
using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Http;
using ParksComputing.XferKit.Scripting;
using ParksComputing.XferKit.Diagnostics;
// using ParksComputing.XferKit.Cli.Services;
// using ParksComputing.XferKit.Cli.Services.Impl;
using ParksComputing.XferKit.DataStore;

namespace ParksComputing.XferKit.Tests;

public static class TestSetup
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddXferKitWorkspaceServices();
        services.AddXferKitHttpServices();
        services.AddXferKitScriptingServices();
        services.AddXferKitDiagnosticsServices("XferKit");
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

        services.AddXferKitDataStore(databasePath);

        return services.BuildServiceProvider();
    }
}
