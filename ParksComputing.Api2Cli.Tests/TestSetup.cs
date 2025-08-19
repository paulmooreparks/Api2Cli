using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Http;
using ParksComputing.Api2Cli.Scripting;
using ParksComputing.Api2Cli.Diagnostics;
using ParksComputing.Api2Cli.Orchestration;
// using ParksComputing.Api2Cli.Cli.Services;
// using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.DataStore;

namespace ParksComputing.Api2Cli.Tests;

public static class TestSetup
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

    // Ensure tests use the bundled Xfer config so init scripts execute as expected
    // Resolve ..\\..\\..\\TestConfigs\\tests.xfer relative to the test assembly output dir
    var baseDir = AppContext.BaseDirectory;
    var testConfigPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "TestConfigs", "tests.xfer"));
    Environment.SetEnvironmentVariable("A2C_WORKSPACE_CONFIG", testConfigPath);

        services.AddApi2CliWorkspaceServices();
        services.AddApi2CliHttpServices();
        services.AddApi2CliScriptingServices();
    services.AddApi2CliOrchestration();
        services.AddApi2CliDiagnosticsServices("Api2Cli");
        // services.AddSingleton<ICommandSplitter, CommandSplitter>();
        // services.AddSingleton<IScriptCliBridge, ScriptCliBridge>();

        string databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.Api2CliDirectoryName,
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
