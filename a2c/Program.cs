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
using System.Globalization;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Commands;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.DataStore;
using ParksComputing.Api2Cli.Orchestration;
using ParksComputing.Api2Cli.Workspace.Models;
using ParksComputing.Api2Cli.Diagnostics.Services.Unified;

namespace ParksComputing.Api2Cli.Cli;

internal class Program {
    static Program() {
    }

    static async Task<int> Main(string[] args) {
    // Internal timing: measure only time spent inside a2c (exclude dotnet host/restore/build)
    var __a2cOverallSw = Stopwatch.StartNew();
    bool __a2cTimings = "1".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), StringComparison.OrdinalIgnoreCase)
                || "true".Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), StringComparison.OrdinalIgnoreCase);
    bool __a2cShowVersionBanner = "1".Equals(Environment.GetEnvironmentVariable("A2C_SHOW_VERSION"), StringComparison.OrdinalIgnoreCase)
                || "true".Equals(Environment.GetEnvironmentVariable("A2C_SHOW_VERSION"), StringComparison.OrdinalIgnoreCase);
    var __a2cBuildSw = new Stopwatch();
    var __a2cConfigWsSw = new Stopwatch();
    var __a2cRunSw = new Stopwatch();
    var __a2cTimingsPrinted = false;
    bool __a2cTimingsBannerPending = __a2cTimings; // delay emission until IConsoleWriter resolved

    IConsoleWriter? __console = null; // will resolve after building container

    IUnifiedDiagnostics? __unified = null; // will resolve after building container

        // Optional verbose diagnostics event echo (disabled by default). Enable with A2C_DIAG_EVENTS=1|true
        bool __a2cDiagEvents = "1".Equals(Environment.GetEnvironmentVariable("A2C_DIAG_EVENTS"), StringComparison.OrdinalIgnoreCase)
            || "true".Equals(Environment.GetEnvironmentVariable("A2C_DIAG_EVENTS"), StringComparison.OrdinalIgnoreCase);
        if (__a2cDiagEvents) {
            DiagnosticListener.AllListeners.Subscribe(new DiagnosticsEventObserver());
        }

    // Fast-path: handle --version/-v before building services (avoid heavy startup)
    // Trigger this regardless of other options (e.g., --config) to prevent full DI initialization on version queries.
    if (args.Any(a => string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase))) {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            var verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
            // Fast path keeps direct Console (DI not built yet); structured diagnostics intentionally skipped here
            Console.WriteLine($"a2c v{verStr}");
            return 0;
        }

    // REPL mode: if no command is provided, Cliffer will enter interactive mode.
    // Keep fast --version path above and allow option-only invocations to still reach REPL.

        // Early parse: capture --config/-c and --packages/-P before services initialize; pass via options, not env vars.
        string? __configRootOpt = null;
        string? __packagesDirOpt = null;
        string? __langOpt = null;
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
        for (int i = 0; i < args.Length; i++) {
            if (string.Equals(args[i], "--lang", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "-L", StringComparison.OrdinalIgnoreCase)) {
                var next = (i + 1) < args.Length ? args[i + 1] : null;
                if (!string.IsNullOrWhiteSpace(next)) { __langOpt = next; }
                break;
            }
        }

        // Resolve desired UI culture: precedence CLI option > env var (A2C_LANG) > system default
        string? cultureCandidate = __langOpt ?? Environment.GetEnvironmentVariable("A2C_LANG");
        if (!string.IsNullOrWhiteSpace(cultureCandidate)) {
            // Normalization helpers (allow short forms)
            string norm = cultureCandidate!.Trim();
            norm = norm switch {
                "en" or "en-GB" or "en-gb" => "en-GB", // prefer en-GB variant when explicitly chosen
                "zh" or "zh-CN" or "zh-cn" or "zh-hans" => "zh-Hans",
                "ms" or "ms-MY" or "ms-my" => "ms",
                "ta" or "ta-IN" or "ta-in" => "ta",
                _ => norm
            };
            try {
                var ci = CultureInfo.GetCultureInfo(norm);
                CultureInfo.CurrentCulture = ci; // number/date formatting if needed
                CultureInfo.CurrentUICulture = ci; // resource lookup
            }
            catch (CultureNotFoundException) {
                // Fall back silently to system culture; early stage so only Console available
                Console.Error.WriteLine($"Warning: culture '{cultureCandidate}' not recognized. Falling back to system default '{CultureInfo.CurrentUICulture.Name}'.");
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
                    var msg = $"--config must be a directory, but a file path was provided: '{p}'. Use '--config '{suggested}' instead.";
                    // Write to stderr so tests (and callers) can detect failure; DI not initialized yet.
                    Console.Error.WriteLine(msg);
                    __unified?.Warn("cli.args", "config.path.notDirectory", ctx: new Dictionary<string, object?> { ["path"] = p, ["suggested"] = suggested });
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
                    var msg = $"--packages must be a directory, but a file path was provided: '{q}'.";
                    // Write to stderr so tests (and callers) can detect failure; DI not initialized yet.
                    Console.Error.WriteLine(msg);
                    __unified?.Warn("cli.args", "packages.path.notDirectory", ctx: new Dictionary<string, object?> { ["path"] = q });
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
                    services.AddSingleton<ILocalizer, ResourceLocalizer>();
                    services.AddSingleton<IConsoleWriter, ConsoleWriter>();

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
        __unified = cli.ServiceProvider.GetService<IUnifiedDiagnostics>();
        __console = cli.ServiceProvider.GetService<IConsoleWriter>();
        // Inject localization delegate for scripting console helper (makes script.console.* resource keys available)
        try {
            var __localizer = cli.ServiceProvider.GetService<ILocalizer>();
            ParksComputing.Api2Cli.Scripting.Services.ConsoleScriptObject.Localize = key => __localizer?.Get(key) ?? key;
        } catch { /* non-fatal */ }
        __unified?.Debug("startup", "container.built");
        __a2cBuildSw.Stop();

        // Emit deferred timings enabled banner via console writer (maintains silent default when env unset)
        if (__a2cTimingsBannerPending) {
            const string enabledMsg = "A2C_TIMINGS: enabled";
            __console?.WriteLine(enabledMsg, category: "cli.timings", code: "enabled");
            // Also mirror as structured timing flag
            __unified?.Info("cli.timings", enabledMsg, code: "enabled");
        }

        // Optional version banner when explicitly requested by env (non fast-path)
        if (__a2cShowVersionBanner) {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            var verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
            __console?.WriteLine($"a2c v{verStr}", category: "cli.version", code: "banner");
            __unified?.Info("cli.version", "banner", ctx: new Dictionary<string, object?> { ["version"] = verStr });
        }

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
                    __console?.WriteLine(timingLine, category: "cli.timings", code: "summary");

                    if (__mirrorTimings) {
                        __unified?.Info("timings.mirror", timingLine);
                    }

                    // unified structured timings
                    __unified?.Timing("overall", __a2cOverallSw.Elapsed.TotalMilliseconds);
                    __unified?.Timing("build", __a2cBuildSw.Elapsed.TotalMilliseconds);
                    __unified?.Timing("configureWorkspaces", __a2cConfigWsSw.Elapsed.TotalMilliseconds);
                    __unified?.Timing("run", __a2cRunSw.Elapsed.TotalMilliseconds);

                    __a2cTimingsPrinted = true;
                }
            } catch (Exception ex) {
                __unified?.Error("timings", "emit.failure", ex: ex);
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

public class DiagnosticsEventObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>> {
    public void OnNext(DiagnosticListener listener) {
        if (listener.Name == Constants.Api2CliDiagnosticsName) {
            // Explicitly subscribe only to events matching specific criteria:
            // listener.Subscribe(this, eventName => eventName.StartsWith("MyEventPrefix"));
            listener.Subscribe(this);
        }
    }

    public void OnNext(KeyValuePair<string, object?> evt) {
    // Only emit event lines when the observer is enabled (A2C_DIAG_EVENTS set before subscription)
    try {
        var console = ParksComputing.Api2Cli.Cli.Services.Utility.GetService<ParksComputing.Api2Cli.Cli.Services.IConsoleWriter>();
        console?.WriteLine($"{evt.Key}:", category: "cli.diag.events", code: "event.name", ctx: new Dictionary<string, object?> { ["event"] = evt.Key });
        console?.WriteLine($"  {evt.Value}", category: "cli.diag.events", code: "event.payload", ctx: new Dictionary<string, object?> { ["event"] = evt.Key, ["payload"] = evt.Value });
    } catch { /* ignore if services not ready */ }
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}
