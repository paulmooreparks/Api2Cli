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
        orchestrator!.Initialize();
        var doWarm = string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP"), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP"), "1", StringComparison.OrdinalIgnoreCase);
        int limit = 25;
        if (int.TryParse(Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP_LIMIT"), out var parsed) && parsed > 0) { limit = parsed; }
        var debug = string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        orchestrator.Warmup(limit, doWarm, debug);

        if (_workspaceService.BaseConfig is not null) {
            foreach (var macro in _workspaceService.BaseConfig.Macros) {
                var macroCommand = new Macro($"{macro.Key}", $"[macro] {macro.Value.Description}", macro.Value.Command);

                _rootCommand.AddCommand(macroCommand);
            }

            foreach (var script in _workspaceService.BaseConfig.Scripts) {
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

                    switch (argType) {
                        case "string":
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case "number":
                            scriptCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case "boolean":
                            scriptCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case "object":
                            scriptCommand.AddArgument(new Argument<object>(argName, argDescription) { Arity = argArity });
                            break;
                        default:
                            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Script {scriptName}: Invalid or unsupported argument type {argType} for argument {argName}");
                            break;
                    }

                    paramList.Add(argument.Name);
                }

                scriptCommand.AddArgument(new Argument<IEnumerable<string>>("params", "Additional arguments.") { Arity = System.CommandLine.ArgumentArity.ZeroOrMore, IsHidden = true });
                scriptCommand.Handler = CommandHandler.Create(int (InvocationContext invocationContext) => {
                    return scriptHandler.Handler(invocationContext, scriptName, null);
                });

                _rootCommand.AddCommand(scriptCommand);
                var paramString = string.Join(", ", paramList);

                // Build a typed parameter list and Func<> signature for C# scripts
                var typedParams = new List<string>();
                var typeArgsOnly = new List<string>();
                foreach (var name in paramList) {
                    if (arguments.TryGetValue(name, out var argDef)) {
                        var csType = argDef.Type switch {
                            "string" => "string",
                            "number" => "double",
                            "boolean" => "bool",
                            "object" => "object?",
                            _ => "object?"
                        };
                        typedParams.Add($"{csType} {name}");
                        typeArgsOnly.Add(csType);
                    }
                    else {
                        typedParams.Add($"object? {name}");
                        typeArgsOnly.Add("object?");
                    }
                }
                var typedParamString = string.Join(", ", typedParams);
                var funcGenericArgs = typeArgsOnly.Count > 0
                    ? string.Join(", ", typeArgsOnly.Concat(new[] { "object?" }))
                    : "object?";

                // Script registration moved to WorkspaceScriptingService
            }
        }

        var workspaceColl = _a2c.Workspaces as IDictionary<string, object>;

        foreach (var workspaceKvp in _workspaceService.BaseConfig?.Workspaces ?? new Dictionary<string, Workspace.Models.WorkspaceDefinition>()) {
            var workspaceName = workspaceKvp.Key;
            var workspaceConfig = workspaceKvp.Value;
            var workspace = workspaceColl![workspaceName] as dynamic;

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

            foreach (var script in workspaceConfig.Scripts) {
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

                    switch (argType) {
                        case "string":
                            scriptCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case "number":
                            scriptCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case "boolean":
                            scriptCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case "object":
                            break;
                        default:
                            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Script {scriptName}: Invalid or unsupported argument type {argType} for argument {argName}");
                            break;
                    }

                    paramList.Add(argument.Name);
                }

                scriptCommand.Handler = CommandHandler.Create(int (InvocationContext invocationContext) => {
                    return scriptHandler.Handler(invocationContext, scriptName, workspaceName);
                });

                workspaceCommand.AddCommand(scriptCommand);

                paramList.Add("params");

                var paramString = string.Join(", ", paramList);

                // Workspace script/wrapper registration moved to WorkspaceScriptingService
            }

            var requests = workspace.requests as IDictionary<string, object>;

            foreach (var requestKvp in workspaceConfig.Requests) {
                var request = requestKvp.Value;
                var requestName = requestKvp.Key;
                request.Name = requestName;
                var description = request.Description ?? $"{request.Method} {request.Endpoint}";
                var scriptCall = $"send {workspaceName} {requestName} --baseurl {workspaceKvp.Value.BaseUrl}";


                var requestCommand = new Command(requestName, $"[request] {description}");
                requestCommand.IsHidden = workspaceConfig.IsHidden;
                var requestHandler = new SendCommand(Utility.GetService<IHttpService>()!, _a2c, _workspaceService, Utility.GetService<IApi2CliScriptEngineFactory>()!, Utility.GetService<IPropertyResolver>(), Utility.GetService<ISettingsService>());
                var requestObj = requests![requestName] as IDictionary<string, object>;
                var requestCaller = new RequestCaller(_rootCommand, requestHandler, workspaceName, requestName, workspaceKvp.Value.BaseUrl);
#pragma warning disable CS8974 // Converting method group to non-delegate type
                requestObj!["execute"] = requestCaller.RunRequest;
#pragma warning restore CS8974 // Converting method group to non-delegate type

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

                    switch (argType) {
                        case "string":
                            requestCommand.AddArgument(new Argument<string>(argName, argDescription) { Arity = argArity });
                            break;
                        case "number":
                            requestCommand.AddArgument(new Argument<double>(argName, argDescription) { Arity = argArity });
                            break;
                        case "boolean":
                            requestCommand.AddArgument(new Argument<bool>(argName, argDescription) { Arity = argArity });
                            break;
                        case "object":
                            requestCommand.AddArgument(new Argument<object>(argName, argDescription) { Arity = argArity });
                            break;
                        default:
                            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Request {requestName}: Invalid or unsupported argument type {argType} for argument {argName}");
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

            foreach (var macro in workspaceConfig.Macros) {
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
