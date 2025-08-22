using System.Text;

using Cliffer;

using Microsoft.Extensions.DependencyInjection;

using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Http;
using ParksComputing.Api2Cli.Scripting;
using ParksComputing.Api2Cli.Diagnostics;
using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Cli;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Commands;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.DataStore;
using ParksComputing.Api2Cli.Orchestration;
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Cli;

internal class Program {
    static Program() {
    }

    static async Task<int> Main(string[] args) {
    // Internal timing: measure only time spent inside a2c (exclude dotnet host/restore/build)
    var __a2cOverallSw = Stopwatch.StartNew();
    bool __a2cTimings = "1".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), StringComparison.OrdinalIgnoreCase)
                || "true".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), StringComparison.OrdinalIgnoreCase);
    var __a2cBuildSw = new Stopwatch();
    var __a2cConfigWsSw = new Stopwatch();
    var __a2cRunSw = new Stopwatch();
    var __a2cTimingsPrinted = false;

    if (__a2cTimings) {
        // Emit a quick marker early so users can verify the flag was recognized
        const string enabledMsg = "A2C_TIMINGS: enabled";
        Console.WriteLine(enabledMsg);
    }

        DiagnosticListener.AllListeners.Subscribe(new MyObserver());

    // Fast-path: handle --version/-v before building services (avoid heavy startup)
    // Trigger this regardless of other options (e.g., --config) to prevent full DI initialization on version queries.
    if (args.Any(a => string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase))) {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            var verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
            Console.WriteLine($"a2c v{verStr}");
            return 0;
        }

    // REPL mode: if no command is provided, Cliffer will enter interactive mode.
    // Keep fast --version path above and allow option-only invocations to still reach REPL.

        // Early parse: capture --config/-c and --packages/-P before services initialize; pass via options, not env vars.
        string? __configRootOpt = null;
        string? __packagesDirOpt = null;
        for (int i = 0; i < args.Length; i++) {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "-c", StringComparison.OrdinalIgnoreCase)) {
                var next = (i + 1) < args.Length ? args[i + 1] : null;
                if (!string.IsNullOrWhiteSpace(next)) { __configRootOpt = next; }
                break;
            }
        }
        for (int i = 0; i < args.Length; i++) {
            if (string.Equals(args[i], "--packages", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "-P", StringComparison.OrdinalIgnoreCase)) {
                var next = (i + 1) < args.Length ? args[i + 1] : null;
                if (!string.IsNullOrWhiteSpace(next)) { __packagesDirOpt = next; }
                break;
            }
        }

        // Validate --config when provided: if the path exists and is a file, fail fast with a clear message.
        if (!string.IsNullOrWhiteSpace(__configRootOpt)) {
            var p = __configRootOpt!;
            if (File.Exists(p) || Directory.Exists(p)) {
                var attr = File.GetAttributes(p);
                if (!attr.HasFlag(FileAttributes.Directory)) {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var suggested = Path.GetDirectoryName(p) ?? Path.Combine(home, Constants.Api2CliDirectoryName);
                    Console.Error.WriteLine($"--config must be a directory, but a file path was provided: '{p}'. Use '--config '{suggested}' instead.");
                    return 2;
                }
            }
        }

        // Validate --packages when provided: if the path exists and is a file, fail fast with a clear message.
        if (!string.IsNullOrWhiteSpace(__packagesDirOpt)) {
            var q = __packagesDirOpt!;
            if (File.Exists(q) || Directory.Exists(q)) {
                var attr = File.GetAttributes(q);
                if (!attr.HasFlag(FileAttributes.Directory)) {
                    Console.Error.WriteLine($"--packages must be a directory, but a file path was provided: '{q}'.");
                    return 2;
                }
            }
        }

    __a2cBuildSw.Start();
    var wsOptions = new WorkspaceRuntimeOptions {
        ConfigRoot = __configRootOpt,
        PackagesDir = __packagesDirOpt
    };

    var cli = new ClifferBuilder()
                .ConfigureServices(services => {
                    // Provide runtime options to workspace services
                    services.AddSingleton(wsOptions);
                    services.AddApi2CliWorkspaceServices();
                    services.AddApi2CliHttpServices();
                    services.AddApi2CliScriptingServices();
                    services.AddApi2CliOrchestration();
                    services.AddApi2CliDiagnosticsServices("Api2Cli");
                    services.AddSingleton<ICommandSplitter, CommandSplitter>();
                    services.AddSingleton<IScriptCliBridge, ScriptCliBridge>();

                    // Align data store path to the selected config root
                    string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.Api2CliDirectoryName);
                    string configRoot = string.IsNullOrWhiteSpace(wsOptions.ConfigRoot) ? defaultRoot : wsOptions.ConfigRoot!;
                    string databasePath = Path.Combine(configRoot, Constants.StoreFileName);

                    if (!Directory.Exists(Path.GetDirectoryName(databasePath))) {
                        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
                    }

                    services.AddApi2CliDataStore(databasePath);
                })
                .Build();
    __a2cBuildSw.Stop();

        Cliffer.Macro.CustomMacroArgumentProcessor += CustomMacroArgumentProcessor;
        Utility.SetServiceProvider(cli.ServiceProvider);
        var rootCommand = cli.RootCommandInstance as A2CRootCommand;

        if (rootCommand is not null) {
            __a2cConfigWsSw.Start();
            rootCommand.ConfigureWorkspaces();
            __a2cConfigWsSw.Stop();
        }

        var __mirrorTimings = "1".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), StringComparison.OrdinalIgnoreCase)
            || "true".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), StringComparison.OrdinalIgnoreCase);

        ClifferEventHandler.OnExit += () => {
            try {
                if (__a2cTimings && !__a2cTimingsPrinted) {

                    if (__a2cRunSw.IsRunning) {
                        __a2cRunSw.Stop();
                    }

                    if (__a2cOverallSw.IsRunning) {
                        __a2cOverallSw.Stop();
                    }

                    var timingLine = $"A2C_TIMINGS: overall={__a2cOverallSw.Elapsed.TotalMilliseconds:F1} ms, build={__a2cBuildSw.Elapsed.TotalMilliseconds:F1} ms, configureWorkspaces={__a2cConfigWsSw.Elapsed.TotalMilliseconds:F1} ms, run={__a2cRunSw.Elapsed.TotalMilliseconds:F1} ms";
                    Console.WriteLine(timingLine);

                    if (__mirrorTimings) {
                        Console.Error.WriteLine(timingLine);
                    }

                    __a2cTimingsPrinted = true;
                }
            } catch (Exception ex) {
                Console.Error.WriteLine($"Error emitting A2C timings: {ex}");
            }
        };

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        __a2cRunSw.Start();
        var exitCode = await cli.RunAsync(args);
        __a2cRunSw.Stop();

    __a2cOverallSw.Stop();

        return exitCode;
    }

    private static string[] CustomMacroArgumentProcessor(string[] args) {
        for (int i = 0; i < args.Length; i++) {
            // Find the first instance of the baseurl option flag and its argument.
            if (args[i] == "-b" || args[i] == "--baseurl") {
                // Index 'i' now points to the first occurrence.
                // Continue the loop with index 'j' starting at 'i + 2'.
                for (int j = i + 2; j < args.Length; ++j) {
                    // If there is a second instance of the baseurl option flag,
                    // remove the first instance and its argument.
                    if (args[j] == "-b" || args[j] == "--baseurl") {
                        var newArgs = new List<string>();

                        // Copy all arguments to a new collection, except the
                        // first and second occurrences.
                        for (int k = 0; k < args.Length; k++) {
                            if (k == i || k == i + 1) {
                                continue;
                            }

                            newArgs.Add(args[k]);
                        }

                        // We only expect to find another instance after the
                        // first one, so early termination is okay.
                        return newArgs.ToArray();
                    }
                }
            }
        }

        return args;
    }
}

public class MyObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>> {
    public void OnNext(DiagnosticListener listener) {
        if (listener.Name == Constants.Api2CliDiagnosticsName) {
            // Explicitly subscribe only to events matching specific criteria:
            // listener.Subscribe(this, eventName => eventName.StartsWith("MyEventPrefix"));
            listener.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> evt) {
        Console.WriteLine($"{evt.Key}: ");
        Console.WriteLine($"  {evt.Value}");
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}
