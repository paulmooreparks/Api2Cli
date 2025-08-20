using Cliffer;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Api;

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.Reflection;

using System.Diagnostics;
using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Cli.Repl;
using ParksComputing.Api2Cli.Http.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Orchestration.Services;
using static ParksComputing.Api2Cli.Orchestration.Services.ScriptArgTypeHelper;


namespace ParksComputing.Api2Cli.Cli.Commands;

[RootCommand("Api2Cli Application")]
[Option(typeof(string), "--config", "Path to an alternate workspaces.xfer configuration file.", new[] { "-c" }, IsRequired = false)]
[Option(typeof(string), "--packages", "Path to the packages directory to use instead of the default (~/.a2c/packages).", new[] { "-P" }, IsRequired = false)]
[Option(typeof(string), "--baseurl", "The base URL of the API to send HTTP requests to.", new[] { "-b" }, IsRequired = false)]
[Option(typeof(bool), "--version", "Display the version information.", new[] { "-v" }, IsRequired = false)]
[Option(typeof(bool), "--recursive", "Indicates if this is a recursive call.", IsHidden = true, IsRequired = false)]
internal class A2CRootCommand {
    private readonly Option _recursionOption;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceService _workspaceService;
    private readonly IReplContext _replContext;
    private readonly System.CommandLine.RootCommand _rootCommand;
    // private readonly IApi2CliScriptEngine _scriptEngine;
    private readonly IApi2CliScriptEngineFactory _scriptEngineFactory;
    private readonly A2CApi _a2c;
    private readonly IScriptCliBridge _scriptCliBridge;
    private readonly ISettingsService _settingsService;

    private string _currentWorkspaceName = string.Empty;

    public A2CRootCommand(
        IServiceProvider serviceProvider,
        IWorkspaceService workspaceService,
        System.CommandLine.RootCommand rootCommand,
        ICommandSplitter splitter,
        IApi2CliScriptEngineFactory scriptEngineFactory,
        A2CApi Api2CliApi,
        IScriptCliBridge scriptCliBridge,
        ISettingsService settingsService,
        [OptionParam("--recursive")] Option recursionOption
        ) {
        _serviceProvider = serviceProvider;
        _workspaceService = workspaceService;
        _rootCommand = rootCommand;
        _scriptEngineFactory = scriptEngineFactory;
        // _scriptEngine = _scriptEngineFactory.GetEngine("javascript");
        _a2c = Api2CliApi;
        _recursionOption = recursionOption;
        _replContext = new A2CReplContext(_rootCommand, _serviceProvider, _workspaceService, splitter, _recursionOption);
        _scriptCliBridge = scriptCliBridge;
        _settingsService = settingsService;
        _scriptCliBridge.RootCommand = rootCommand;
    }

    public void ConfigureWorkspaces() {
        // Initialize scripts via orchestration layer (decoupled from Workspace)
        var orchestrator = Utility.GetService<IWorkspaceScriptingOrchestrator>();

        var timingsEnabled = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "1", StringComparison.OrdinalIgnoreCase);
        System.Diagnostics.Stopwatch? swScripting = null;
        System.Diagnostics.Stopwatch? swInit = null;
        System.Diagnostics.Stopwatch? swWarm = null;
        if (timingsEnabled) {
            swScripting = System.Diagnostics.Stopwatch.StartNew();
            swInit = System.Diagnostics.Stopwatch.StartNew();
        }

        orchestrator!.Initialize();

        if (timingsEnabled && swInit is not null) {
            var ms = swInit.Elapsed.TotalMilliseconds;
            var line = $"A2C_TIMINGS: scriptingInit={ms:F1} ms";
            var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
            try { Console.WriteLine(line); } catch { }
            if (mirror) { try { Console.Error.WriteLine(line); } catch { } }
        }
        var doWarm = string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP"), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP"), "1", StringComparison.OrdinalIgnoreCase);
        int limit = 25;
        if (int.TryParse(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP_LIMIT"), out var parsed) && parsed > 0) { limit = parsed; }
        var debug = string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        if (timingsEnabled) { swWarm = System.Diagnostics.Stopwatch.StartNew(); }
        orchestrator.Warmup(limit, doWarm, debug);

        if (timingsEnabled && swWarm is not null) {
            var ms = swWarm.Elapsed.TotalMilliseconds;
            var line = $"A2C_TIMINGS: scriptingWarmup={ms:F1} ms";
            var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
            try { Console.WriteLine(line); } catch { }
            if (mirror) { try { Console.Error.WriteLine(line); } catch { } }
        }

    if (timingsEnabled && swScripting is not null) {
            var ms = swScripting.Elapsed.TotalMilliseconds;
            var line = $"A2C_TIMINGS: scriptingSetup={ms:F1} ms";
            var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
            try { Console.WriteLine(line); } catch { }
            if (mirror) { try { Console.Error.WriteLine(line); } catch { } }
        }

        // Register a single request executor bridge once; avoid per-request registration which causes O(N^2) wiring
        orchestrator.RegisterRequestExecutor((wsName, reqName, args) => {
            var http = Utility.GetService<IHttpService>()!;
            var a2c = _a2c;
            var wsService = _workspaceService;
            var factory = Utility.GetService<IApi2CliScriptEngineFactory>()!;
            var orch = Utility.GetService<ParksComputing.Api2Cli.Orchestration.Services.IWorkspaceScriptingOrchestrator>()!;
            var prop = Utility.GetService<IPropertyResolver>()!;
            var settings = Utility.GetService<ISettingsService>()!;

            if (!wsService.BaseConfig.Workspaces.TryGetValue(wsName, out var wsDef)) {
                return null;
            }
            var baseUrl = wsDef.BaseUrl;
            var send = new SendCommand(http, a2c, wsService, factory, orch, prop, settings);
            var caller = new RequestCaller(_rootCommand, send, wsName, reqName, baseUrl);
            return caller.RunRequest(args ?? Array.Empty<object?>());
        });

        if (_workspaceService.BaseConfig is not null) {
            foreach (var macro in _workspaceService.BaseConfig.Macros.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase)) {
                var macroCommand = new Macro($"{macro.Key}", $"[macro] {macro.Value.Description}", macro.Value.Command);

                _rootCommand.AddCommand(macroCommand);
            }

            foreach (var script in _workspaceService.BaseConfig.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)) {
                var scriptName = script.Key;
                var (scriptLanguage, scriptBody) = script.Value.ResolveLanguageAndBody();
                var description = script.Value.Description ?? string.Empty;
                var arguments = script.Value.Arguments;
                var paramList = new List<string>();

                var scriptCommand = new Command(scriptName, $"[script] {description}");
                var scriptEngine = _scriptEngineFactory.GetEngine(scriptLanguage);
                var scriptHandler = new RunWsScriptCommand(_workspaceService, _scriptEngineFactory, scriptEngine);

                foreach (var kvp in arguments) {
                    var argument = kvp.Value;
                    var argType = argument.Type;
                    var argName = kvp.Key;
                    argument.Name = argName;
                    var argDescription = argument.Description;
                    System.CommandLine.ArgumentArity argArity = argument.IsRequired ? System.CommandLine.ArgumentArity.ExactlyOne : System.CommandLine.ArgumentArity.ZeroOrOne;

                    switch (GetArgKind(argType)) {
                        case ScriptArgKind.String:
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Number:
                            scriptCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Boolean:
                            scriptCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Object:
                            scriptCommand.AddArgument(new Argument<object>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Custom:
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                    }

                    paramList.Add(argument.Name);
                }

                scriptCommand.AddArgument(new Argument<IEnumerable<string>>("params", "Additional arguments.") { Arity = System.CommandLine.ArgumentArity.ZeroOrMore, IsHidden = true });
                scriptCommand.Handler = CommandHandler.Create(int (InvocationContext invocationContext) => {
                    return scriptHandler.Handler(invocationContext, scriptName, null);
                });

                _rootCommand.AddCommand(scriptCommand);
                // Script registration handled by the orchestrator
            }
        }

    foreach (var workspaceKvp in (_workspaceService.BaseConfig?.Workspaces ?? new Dictionary<string, Workspace.Models.WorkspaceDefinition>())
             .OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase)) {
            var workspaceName = workspaceKvp.Key;
            var workspaceConfig = workspaceKvp.Value;

            var workspaceCommand = new Command(workspaceName, $"[workspace] {workspaceConfig.Description}");
            workspaceCommand.IsHidden = workspaceConfig.IsHidden;
            var workspaceHandler = new WorkspaceCommand(workspaceName, _rootCommand, _serviceProvider, _workspaceService);

            var baseurlOption = new Option<string>(["--baseurl", "-b"], "The base URL of the API to send HTTP requests to.");
            baseurlOption.IsRequired = false;
            workspaceCommand.AddOption(baseurlOption);

            workspaceCommand.Handler = CommandHandler.Create(async Task<int> (InvocationContext invocationContext) => {
                var baseUrlArg = workspaceCommand.Options.FirstOrDefault(a => a.Name == "baseurl");
                var baseUrl = workspaceConfig.BaseUrl;

                if (baseUrlArg is not null) {
                    baseUrl = invocationContext.ParseResult.GetValueForOption(baseUrlArg)?.ToString() ?? workspaceConfig.BaseUrl;
                }

                if (string.IsNullOrEmpty(baseUrl)) {
                    Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Error: Invalid base URL: {baseUrl}");
                    return Result.InvalidArguments;
                }

                return await workspaceHandler.Execute(workspaceCommand, invocationContext, baseUrl);
            });

            _rootCommand.AddCommand(workspaceCommand);

            foreach (var script in workspaceConfig.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)) {
                var scriptName = script.Key;
                var (scriptLanguage, scriptBody) = script.Value.ResolveLanguageAndBody();
                var description = script.Value.Description ?? string.Empty;
                var arguments = script.Value.Arguments;
                var paramList = new List<string>();

                var scriptCommand = new Command(scriptName, $"[script] {description}");
                scriptCommand.IsHidden = workspaceConfig.IsHidden;
                var scriptEngine = _scriptEngineFactory.GetEngine(scriptLanguage);
                var scriptHandler = new RunWsScriptCommand(_workspaceService, _scriptEngineFactory, scriptEngine);

                foreach (var kvp in arguments) {
                    var argument = kvp.Value;
                    var argType = argument.Type;
                    var argName = kvp.Key;
                    argument.Name = argName;
                    var argDescription = argument.Description;
                    System.CommandLine.ArgumentArity argArity = argument.IsRequired ? System.CommandLine.ArgumentArity.ExactlyOne : System.CommandLine.ArgumentArity.ZeroOrOne;

                    switch (GetArgKind(argType)) {
                        case ScriptArgKind.String:
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Number:
                            scriptCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Boolean:
                            scriptCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Object:
                            scriptCommand.AddArgument(new Argument<object>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Custom:
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                    }

                    paramList.Add(argument.Name);
                }

                scriptCommand.Handler = CommandHandler.Create(int (InvocationContext invocationContext) => {
                    return scriptHandler.Handler(invocationContext, scriptName, workspaceName);
                });

                workspaceCommand.AddCommand(scriptCommand);

                paramList.Add("params");
                // Workspace script/wrapper registration handled by the orchestrator
            }

            foreach (var requestKvp in workspaceConfig.Requests.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)) {
                var request = requestKvp.Value;
                var requestName = requestKvp.Key;
                request.Name = requestName;
                var description = request.Description ?? $"{request.Method} {request.Endpoint}";
                // CLI help text constructed by System.CommandLine; orchestrator handles execution wiring


                var requestCommand = new Command(requestName, $"[request] {description}");
                requestCommand.IsHidden = workspaceConfig.IsHidden;
                var requestHandler = new SendCommand(Utility.GetService<IHttpService>()!, _a2c, _workspaceService, Utility.GetService<IApi2CliScriptEngineFactory>()!, Utility.GetService<ParksComputing.Api2Cli.Orchestration.Services.IWorkspaceScriptingOrchestrator>()!, Utility.GetService<IPropertyResolver>(), Utility.GetService<ISettingsService>());
                var requestCaller = new RequestCaller(_rootCommand, requestHandler, workspaceName, requestName, workspaceKvp.Value.BaseUrl);

                var reqBaseurlOption = new Option<string>(["--baseurl", "-b"], "The base URL of the API to send HTTP requests to.");
                reqBaseurlOption.IsRequired = false;
                requestCommand.AddOption(reqBaseurlOption);

                var parameterOption = new Option<IEnumerable<string>>(["--parameters", "-p"], "Query parameters to include in the request. If input is redirected, parameters can also be read from standard input.");
                parameterOption.AllowMultipleArgumentsPerToken = true;
                parameterOption.Arity = System.CommandLine.ArgumentArity.ZeroOrMore;
                requestCommand.AddOption(parameterOption);

                var headersOption = new Option<IEnumerable<string>>(["--headers", "-h"], "Headers to include in the request.");
                headersOption.AllowMultipleArgumentsPerToken = true;
                headersOption.Arity = System.CommandLine.ArgumentArity.ZeroOrMore;
                requestCommand.AddOption(headersOption);

                var payloadOption = new Option<string>(["--payload", "-pl"], "Content to send with the request. If input is redirected, content can also be read from standard input.");
                payloadOption.Arity = System.CommandLine.ArgumentArity.ZeroOrOne;
                requestCommand.AddOption(payloadOption);

                foreach (var kvp in request.Arguments) {
                    var argName = kvp.Key;
                    var argument = kvp.Value;
                    argument.Name = argName;
                    var argType = argument.Type;
                    var argDescription = argument.Description;
                    System.CommandLine.ArgumentArity argArity = argument.IsRequired ? System.CommandLine.ArgumentArity.ExactlyOne : System.CommandLine.ArgumentArity.ZeroOrOne;

                    switch (GetArgKind(argType)) {
                        case ScriptArgKind.String:
                            requestCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Number:
                            requestCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Boolean:
                            requestCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Object:
                            requestCommand.AddArgument(new Argument<object>(argName, argDescription) { Arity = argArity });
                            break;
                        case ScriptArgKind.Custom:
                            requestCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                    }
                }

                requestCommand.Handler = CommandHandler.Create(int (InvocationContext invocationContext) => {
                    var baseUrlArg = workspaceCommand.Options.FirstOrDefault(a => a.Name == "baseurl");
                    var baseUrl = workspaceConfig.BaseUrl;

                    if (baseUrlArg is not null) {
                        baseUrl = invocationContext.ParseResult.GetValueForOption(baseUrlArg)?.ToString() ?? workspaceConfig.BaseUrl;
                    }

                    if (string.IsNullOrEmpty(baseUrl)) {
                        Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Error: Invalid base URL: {baseUrl}");
                        return Result.InvalidArguments;
                    }

                    return requestHandler.Handler(invocationContext, workspaceName, requestName, baseUrl, null, null, null, null, null, null);
                });

                workspaceCommand.AddCommand(requestCommand);
            }

            foreach (var macro in workspaceConfig.Macros.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase)) {
                var macroCommand = new Macro($"{workspaceName}.{macro.Key}", $"[macro] {macro.Value.Description}", macro.Value.Command);
                macroCommand.IsHidden = workspaceConfig.IsHidden;

                workspaceCommand.AddCommand(macroCommand);
            }
        }
    }

    public async Task<int> Execute(
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--version")] bool showVersion,
        [OptionParam("--recursive")] bool isRecursive,
        Command command,
        InvocationContext context
        ) {
    // Note: The --config override is applied early in Program.Main via env var
    // A2C_WORKSPACE_CONFIG so services (SettingsService/WorkspaceService) pick it up
    // before initialization. Keeping it here ensures help/usage shows the option.
    // Similarly, --packages is early-parsed and stored in A2C_PACKAGES_DIR.
        if (showVersion) {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version? version = assembly.GetName().Version;
            string versionString = version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "Unknown";
            Console.WriteLine($"a2c v{versionString}");
            return Result.Success;
        }

        if (baseUrl is not null) {
            _workspaceService.ActiveWorkspace.BaseUrl = baseUrl;
        }

        if (isRecursive) {
            return Result.Success;
        }

        var result = await command.Repl(
            _serviceProvider,
            context,
            _replContext
            );

        return result;
    }
}
