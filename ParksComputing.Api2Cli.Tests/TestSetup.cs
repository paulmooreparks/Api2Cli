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
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Tests;

public static class TestSetup
{
    public static IServiceProvider ConfigureServices()
    {
    var services = new ServiceCollection();

    // Ensure tests use the bundled Xfer config so init scripts execute as expected
    // Resolve ..\\..\\..\\TestConfigs\\tests.xfer relative to the test assembly output dir
    var baseDir = AppContext.BaseDirectory;
    var testXferPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "TestConfigs", "tests.xfer"));

    // Create an isolated config root for tests and place tests.xfer as config.xfer
    var tempRoot = Path.Combine(Path.GetTempPath(), "a2c-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var configPath = Path.Combine(tempRoot, "config.xfer");
    File.Copy(testXferPath, configPath, overwrite: true);
    Directory.CreateDirectory(Path.Combine(tempRoot, "workspaces"));

    // Provide options to workspace services (no environment variables)
    services.AddSingleton(new WorkspaceRuntimeOptions { ConfigRoot = tempRoot });

        services.AddApi2CliWorkspaceServices();
        services.AddApi2CliHttpServices();
        services.AddApi2CliScriptingServices();
    services.AddApi2CliOrchestration();
        services.AddApi2CliDiagnosticsServices("Api2Cli");
        // services.AddSingleton<ICommandSplitter, CommandSplitter>();
        // services.AddSingleton<IScriptCliBridge, ScriptCliBridge>();

    // Align the test data store path with the test config root
    string databasePath = Path.Combine(tempRoot, Constants.StoreFileName);

        if (!Directory.Exists(Path.GetDirectoryName(databasePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }

        services.AddApi2CliDataStore(databasePath);

        return services.BuildServiceProvider();
    }
}
