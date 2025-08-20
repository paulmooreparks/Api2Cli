using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.ClearScript;

using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Workspace.Models;
using System.Net.Http.Headers;
using Microsoft.ClearScript.V8;
using System.Dynamic;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Scripting.Extensions;
using ParksComputing.Xfer.Lang;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl;

internal class ClearScriptEngine : IApi2CliScriptEngine {
    private bool _isInitialized = false;
    private readonly IPackageService _packageService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly IAppDiagnostics<ClearScriptEngine> _diags;
    private readonly IPropertyResolver _propertyResolver;
    private readonly A2CApi _a2c;

    // V8 engine is created lazily in InitializeScriptEnvironment so we can configure flags based on env
    private V8ScriptEngine? _engine;
    private V8ScriptEngine Engine => _engine ??= CreateV8Engine();
    private static bool ScriptDebug => string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

    public ClearScriptEngine(
        IPackageService packageService,
        IWorkspaceService workspaceService,
        IStoreService storeService,
        ISettingsService settingsService,
        IAppDiagnostics<ClearScriptEngine> appDiagnostics,
        IPropertyResolver propertyResolver,
        A2CApi apiRoot
        ) {
        _workspaceService = workspaceService;
        _packageService = packageService;
        _packageService.PackagesUpdated += PackagesUpdated;
        _settingsService = settingsService;
        _diags = appDiagnostics;
        _propertyResolver = propertyResolver;
        _a2c = apiRoot;
    }

    public dynamic Script => Engine.Script;

    private void PackagesUpdated() {
        LoadPackageAssemblies();
    }

    private IEnumerable<Assembly> LoadPackageAssemblies() {
        var assemblies = new List<Assembly>();
        var packageAssemblies = _packageService.GetInstalledPackagePaths();

        foreach (var assemblyPath in packageAssemblies) {
            try {
                var assembly = Assembly.LoadFrom(assemblyPath);
                if (assembly != null) {
                    var name = assembly.GetName().Name;
                    assemblies.Add(assembly);
                }
            }
            catch (Exception ex) {
                throw new Exception($"{Constants.ErrorChar} Failed to load package assembly {assemblyPath}: {ex.Message}", ex);
            }
        }

        // var langAssembly = Assembly.Load("ParksComputing.Api2Cli.Lang");
        // assemblies.Add(langAssembly);
        return assemblies;
    }

    private IEnumerable<Assembly> LoadConfiguredAssemblies() {
        var assemblies = new List<Assembly>();
        try {
            var names = _workspaceService?.BaseConfig?.Assemblies;
            if (names is null || names.Length == 0) {
                return assemblies;
            }

            string? pluginDir = null;
            try { pluginDir = _settingsService?.PluginDirectory; } catch { /* ignore */ }

            foreach (var name in names) {
                try {
                    var path = name;
                    if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(pluginDir)) {
                        path = Path.Combine(pluginDir!, name);
                    }
                    if (!File.Exists(path)) {
                        _diags.Emit(nameof(ClearScriptEngine), new { Message = $"Workspace assembly not found: {path}" });
                        continue;
                    }
                    var asm = Assembly.LoadFrom(path);
                    assemblies.Add(asm);
                }
                catch (Exception ex) {
                    _diags.Emit(nameof(ClearScriptEngine), new { Message = $"Failed to load workspace assembly '{name}': {ex.Message}" });
                }
            }
        }
        catch (Exception ex) {
            _diags.Emit(nameof(ClearScriptEngine), new { Message = $"Error loading configured assemblies: {ex.Message}" });
        }
        return assemblies;
    }

    private readonly Dictionary<string, dynamic> _workspaceCache = new();
    private readonly Dictionary<string, dynamic> _requestCache = new();
    private readonly HashSet<string> _definedRequestWrappers = new(StringComparer.OrdinalIgnoreCase);

    private static string ReqKey(string ws, string req) => ws + "|" + req;

    private V8ScriptEngine CreateV8Engine() {
        var debug = string.Equals(Environment.GetEnvironmentVariable("A2C_JS_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("A2C_JS_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        var flags = V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableValueTaskPromiseConversion;
        if (debug) {
            flags |= V8ScriptEngineFlags.EnableDebugging;
        }
        return new V8ScriptEngine(flags);
    }

    public void InitializeScriptEnvironment() {
    if (_isInitialized) {
            return;
        }
    // Create the V8 engine lazily with flags per environment
    _engine ??= CreateV8Engine();

    var assemblies = LoadPackageAssemblies().ToList();
    assemblies.AddRange(LoadConfiguredAssemblies());
    if (ScriptDebug) {
        try { Console.Error.WriteLine("[JS:init] Begin InitializeScriptEnvironment"); } catch { }
    }

        // _engine = new Engine(options => options.AllowClr(assemblies.ToArray()));
    Engine.AddHostObject("host", new ExtendedHostFunctions());

        // Expose selected host types. Provide default 'clr' host type collection for scripts.
        // Include simple names of any loaded package/plugin assemblies so scripts can access types via clr.
        var hostAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "mscorlib", "System", "System.Core", "ParksComputing.Api2Cli.Workspace"
        };
        foreach (var asm in assemblies) {
            try {
                var simple = asm.GetName().Name;
                if (!string.IsNullOrEmpty(simple)) {
                    hostAssemblyNames.Add(simple);
                }
            } catch { /* ignore */ }
        }
    var typeCollection = new HostTypeCollection(hostAssemblyNames.ToArray());
    // Do not predeclare a global named 'clr' to avoid collisions with user scripts (e.g., `let clr = ...`).
    // Instead, expose only a fallback collection as 'defaultClr' and assign 'clr' later if missing.
    Engine.AddHostObject("defaultClr", typeCollection);

    Engine.AddHostType("Console", typeof(Console));
    Engine.AddHostType("Task", typeof(Task));
    Engine.AddHostType("console", typeof(ConsoleScriptObject));
    Engine.AddHostType("Environment", typeof(Environment));
        // _engine.AddHostObject("workspaceService", _workspaceService);
    Engine.AddHostObject("btoa", new Func<string, string>(s => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))));
    Engine.AddHostObject("atob", new Func<string, string>(s => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s))));

    Engine.AddHostObject("a2c", _a2c);
        dynamic da2c = _a2c;

        if (_workspaceService is not null && _workspaceService.BaseConfig is not null && _workspaceService?.BaseConfig.Workspaces is not null) {
            foreach (var kvp in _workspaceService.BaseConfig.Properties) {
                if (!_a2c.TrySetProperty(kvp.Key, kvp.Value)) {
                    _diags.Emit(
                        nameof(ClearScriptEngine),
                        new {
                            Message = $"Failed to set property {kvp.Key} to {kvp.Value}"
                        }
                    );
                }
            }

            // Define base handlers; defer global init execution until after projecting workspaces/requests
            try {
                var globalPreRequestJs = GetJsScriptBody(_workspaceService.BaseConfig.PreRequest);
                Engine.Execute(
    $@"
function __preRequest(workspace, request) {{
    {GetScriptContent(globalPreRequestJs)}
}};
");
            }
            catch (ScriptEngineException ex) {
                Console.Error.WriteLine(ex.ErrorDetails);
            }

            var postResponseScriptContent = GetJsScriptBody(_workspaceService.BaseConfig.PostResponse);

            if (string.IsNullOrEmpty(postResponseScriptContent)) {
                postResponseScriptContent = "return request.response.body;";
            }

            try {
                Engine.Execute(
    $@"
function __postResponse(workspace, request) {{
    {postResponseScriptContent}
}};
");
            }
            catch (ScriptEngineException ex) {
                Console.Error.WriteLine(ex.ErrorDetails);
            }

            foreach (var workspaceKvp in _workspaceService.BaseConfig.Workspaces) {
                var workspaceName = workspaceKvp.Key;
                var workspace = workspaceKvp.Value;

                if (!string.IsNullOrEmpty(workspace.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(workspace.Extend, out var baseWorkspace)) {
                    workspace.Base = baseWorkspace;
                }

                var workspaceObj = new ExpandoObject() as dynamic;
                workspaceObj.name = workspace.Name ?? workspaceName;
                workspaceObj.extend = workspace.Extend;
                workspaceObj.baseWorkspace = workspace.Base;
                workspaceObj.baseUrl = workspace.BaseUrl; //?.ReplaceApi2CliPlaceholders(_propertyResolver, _settingsService) ?? "";
                workspaceObj.requests = new ExpandoObject() as dynamic;

                foreach (var kvp in workspace.Properties) {
                    var workspaceDict = workspaceObj as IDictionary<string, object>;

                    if (workspaceDict == null) {
                        throw new InvalidOperationException("Failed to cast workspaceObj to IDictionary<string, object>");
                    }

                    if (workspaceDict.ContainsKey(kvp.Key)) {
                        _diags.Emit(
                            nameof(ClearScriptEngine),
                            new {
                                Message = $"Failed to set property {kvp.Key} to {kvp.Value} in workspace {workspaceName}"
                            }
                        );
                    }
                    else {
                        workspaceDict.Add(kvp.Key, kvp.Value);
                    }
                }

                _workspaceCache.Add(workspaceName, workspaceObj);
                (_a2c.Workspaces as IDictionary<string, object?>)!.Add(workspaceName, workspaceObj);
                _a2c[workspaceName] = workspaceObj;

                try {
                    Engine.Execute($@"
function __preRequest__{workspaceName}(workspace, request) {{
    // Defensive globals for implicit access
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ __preRequest(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? "" : $"__preRequest__{workspace.Extend}(workspace, request);")} }};
    let base = {{ preRequest: function() {{ baseHandler(); }}, postResponse: function() {{ return {(string.IsNullOrEmpty(workspace.Extend) ? "null" : $"__postResponse__{workspace.Extend}(workspace, request)")}; }} }};
    {((workspace.PreRequest == null || !IsJavaScript(workspace.PreRequest)) ? $"__preRequest(workspace, request)" : GetScriptContent(workspace.PreRequest!.PayloadAsString))}
}};

function __postResponse__{workspaceName}(workspace, request) {{
    // Defensive globals for implicit access
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ return __postResponse(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? "return null;" : $"return __postResponse__{workspace.Extend}(workspace, request);")} }};
    let base = {{ preRequest: function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? ";" : $"__preRequest__{workspace.Extend}(workspace, request);")} }}, postResponse: function() {{ return baseHandler(); }} }};
    {((workspace.PostResponse == null || !IsJavaScript(workspace.PostResponse)) ? $"return __postResponse(workspace, request)" : GetScriptContent(workspace.PostResponse!.PayloadAsString))}
}};

");
                }
                catch (ScriptEngineException ex) {
                    Console.Error.WriteLine(ex.ErrorDetails);
                }

                // Populate requests within workspaceKvp
                foreach (var request in workspace.Requests) {
                    var requestName = request.Key;

                    var requestDef = request.Value;

                    // Defer defining per-request wrappers to first use for faster startup
                    // They will be generated on demand in InvokePreRequest/InvokePostResponse

                    dynamic requestObj = new ExpandoObject { } as dynamic;

                    requestObj.name = requestDef.Name ?? string.Empty;
                    requestObj.endpoint = requestDef.Endpoint ?? string.Empty;
                    requestObj.method = requestDef.Method ?? "GET";
                    requestObj.headers = new ExpandoObject();
                    requestObj.parameters = requestDef.Parameters ?? new List<string>();
                    requestObj.payload = requestDef.Payload ?? string.Empty;
                    requestObj.response = new ResponseDefinition();

                    if (requestDef.Properties is not null) {
                        foreach (var kvp in requestDef.Properties) {
                            var requestDict = requestObj as IDictionary<string, object>;

                            if (requestDict == null) {
                                throw new InvalidOperationException("Failed to cast requestObj to IDictionary<string, object>");
                            }

                            if (requestDict.ContainsKey(kvp.Key)) {
                                _diags.Emit(
                                    nameof(ClearScriptEngine),
                                    new {
                                        Message = $"Failed to set property {kvp.Key} to {kvp.Value} in request {workspaceName}.{requestName}"
                                    }
                                );
                            }
                            else {
                                requestDict.Add(kvp.Key, kvp.Value);
                            }
                        }
                    }

                    // Add the request object to the workspace's requests map
                    var wsRequests = workspaceObj.requests as IDictionary<string, object>;
                    if (wsRequests is null) {
                        throw new InvalidOperationException("Failed to cast workspaceObj.requests to IDictionary<string, object>");
                    }
                    wsRequests[requestName] = requestObj;

                    // Also expose each request as a direct property on the workspace object for
                    // backward compatibility with workspace.<requestName>.execute() usage.
                    var wsRootDict = workspaceObj as IDictionary<string, object>;
                    if (wsRootDict is null) {
                        throw new InvalidOperationException("Failed to cast workspaceObj to IDictionary<string, object>");
                    }
                    // Only add if not already present to avoid clobbering any explicit workspace property
                    if (!wsRootDict.ContainsKey(requestName)) {
                        wsRootDict[requestName] = requestObj;
                    }

                    // Ensure an execute() shim exists on both the requests map entry and the direct alias.
                    // This makes workspace.<request>.execute() available even before the orchestrator wires
                    // the host-side request executor. The shim defers to a2cRequestInvoke if present.
                    try {
                        Engine.Execute($@"(function() {{
    try {{
        var ws = a2c.workspaces['{workspaceName}'];
        var reqName = '{requestName}';
        var obj = ws && ws.requests ? ws.requests[reqName] : undefined;
        if (obj) {{
            // Keep alias in sync with the same object reference
            if (ws[reqName] !== obj) ws[reqName] = obj;
            if (typeof obj.execute !== 'function') {{
                obj.execute = function() {{
                    if (typeof a2cRequestInvoke === 'function') {{
                        return a2cRequestInvoke('{workspaceName}', '{requestName}', Array.from(arguments));
                    }}
                    throw new Error('Request executor is not available yet.');
                }};
            }}
            // Provide a shorthand alias .exec that defers to .execute
            if (typeof obj.exec !== 'function') {{
                obj.exec = function() {{ return obj.execute.apply(obj, arguments); }};
            }}
        }}
    }} catch (e) {{ /* best-effort; orchestrator will wire later as well */ }}
}})();");
                    }
                    catch (ScriptEngineException) { /* ignore; best-effort */ }
                }

            }

            // Run optional legacy global init (JavaScript) once at top-level so functions persist globally.
            // If new scriptInit is provided, the orchestrator already executed it; skip legacy global here to avoid duplicates.
            if (_workspaceService?.BaseConfig is not null) {
                var hasNewScriptInit = _workspaceService.BaseConfig.ScriptInit is not null;
                if (!hasNewScriptInit) {
                    if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] Legacy global init path"); } catch { } }
                    var jsInit = GetJsScriptBody(_workspaceService.BaseConfig.InitScript);
                    if (!string.IsNullOrWhiteSpace(jsInit)) {
                        // Bind 'workspace' to active workspace if available (best-effort)
                        try {
                            var wsDict = _a2c.Workspaces as IDictionary<string, object?>;
                            var activeName = _a2c.CurrentWorkspaceName;
                            if (wsDict != null && !string.IsNullOrEmpty(activeName) && wsDict.ContainsKey(activeName)) {
                                var active = wsDict[activeName];
                                if (active != null) { Engine.Script.workspace = active; }
                            }
                        }
                        catch { /* best-effort only for binding workspace */ }

                        var code = GetScriptContent(jsInit);
                        if (!string.IsNullOrWhiteSpace(code)) {
                            // Provide a writable identifier so scripts can reference `workspace` directly.
                            try { Engine.Execute("try { var workspace = this.workspace; } catch (e) {}"); } catch { /* ignore */ }
                            if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] Executing legacy global init"); } catch { } }
                            Engine.Execute(code);
                        }
                    }
                    // For legacy global init (no grouped scriptInit), execute per-workspace inits now (base-first, cycle-safe)
                    foreach (var wsInit in _workspaceService.BaseConfig.Workspaces) {
                        var wsName = wsInit.Key;
                        var wsDef = wsInit.Value;
                        if (_workspaceCache.TryGetValue(wsName, out var wsObj)) {
                            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Execute per-workspace init (legacy) -> {wsName}"); } catch { } }
                            ExecuteWorkspaceInitChain(wsName, wsDef, wsObj);
                        }
                    }
                }
                // When grouped scriptInit is present, defer per-workspace init; orchestrator will run it after global init
            }

            // Ensure default JS 'clr' bridge exists if user didn't provide one in init
            try {
                Engine.Execute("if (typeof clr === 'undefined') { clr = defaultClr; }");
            }
            catch (ScriptEngineException ex) {
                Console.Error.WriteLine(ex.ErrorDetails);
            }

            _isInitialized = true;
            if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] InitializeScriptEnvironment complete"); } catch { } }
        }
    }

    // Invoked by orchestrator when grouped scriptInit is present to ensure workspace JS init runs
    public void ExecuteAllWorkspaceInitScripts()
    {
        if (_workspaceService?.BaseConfig is null) { return; }
    if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] ExecuteAllWorkspaceInitScripts begin"); } catch { } }
        foreach (var wsInit in _workspaceService.BaseConfig.Workspaces) {
            var wsName = wsInit.Key;
            var wsDef = wsInit.Value;
            if (_workspaceCache.TryGetValue(wsName, out var wsObj)) {
        if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Execute per-workspace init (grouped) -> {wsName}"); } catch { } }
                ExecuteWorkspaceInitChain(wsName, wsDef, wsObj);
            }
        }
    if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] ExecuteAllWorkspaceInitScripts end"); } catch { } }
    }

    public void AddHostObject(string itemName, object? target) {
        // Interface allows null; ClearScript expects a non-null target.
        // If target is null, no-op to avoid throwing and to match nullable contract.
        if (target is null) {
            return;
        }
    Engine.AddHostObject(itemName, HostItemFlags.None, target);
    }

    protected void DefineInitScript(string workspaceKey, WorkspaceDefinition workspace, dynamic workspaceObj) {
        // Prefer new per-workspace scriptInit.javascript; fall back to legacy keyed initScript when absent
        string? jsInit = null;
        if (workspace.ScriptInit is not null && !string.IsNullOrWhiteSpace(workspace.ScriptInit.JavaScript)) {
            jsInit = workspace.ScriptInit.JavaScript;
        }
        else {
            jsInit = GetJsScriptBody(workspace.InitScript);
        }
        if (!string.IsNullOrWhiteSpace(jsInit)) {
            var scriptCode = GetScriptContent(jsInit);
        if (!string.IsNullOrWhiteSpace(scriptCode)) {
                try {
            // Bind the workspace object so scripts can read/write it and run at top-level so
            // function/var declarations persist for subsequent scripts in the same engine.
            try { Engine.Script.workspace = workspaceObj; } catch { }
            // Also bind a real identifier for `workspace` so bare references succeed even under stricter resolution rules.
            try { Engine.Execute("try { var workspace = this.workspace; } catch (e) {}"); } catch { /* ignore */ }
            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Running init for workspace '{workspaceKey}'"); } catch { } }
            Engine.Execute(scriptCode);
                }
                catch (ScriptEngineException ex) {
                    Console.Error.WriteLine(ex.ErrorDetails);
                }
            }
        }
    }

    // Execute per-workspace init in base-first order, iteratively, with cycle detection
    private void ExecuteWorkspaceInitChain(string workspaceKey, WorkspaceDefinition workspace, dynamic workspaceObj)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chain = new Stack<(string Key, WorkspaceDefinition Def)>();

        string currentKey = workspaceKey;
        var current = workspace;
        while (current != null) {
            if (!visited.Add(currentKey)) {
                // Cycle detected in workspace inheritance; emit and break to avoid infinite loop
                try { _diags.Emit(nameof(ClearScriptEngine), new { Message = $"Cycle detected in workspace inheritance starting at '{workspaceKey}'. Halting init chain at '{currentKey}'." }); } catch { }
                if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Cycle detected: start={workspaceKey}, at={currentKey}. Aborting chain."); } catch { } }
                break;
            }
            chain.Push((currentKey, current));
            if (current.Base != null) {
                currentKey = current.Extend ?? currentKey;
                current = current.Base;
            } else {
                break;
            }
        }

        // Unwind base-first
        while (chain.Count > 0) {
            var (k, def) = chain.Pop();
            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] DefineInitScript -> {k}"); } catch { } }
            DefineInitScript(k, def, workspaceObj);
        }
    }

    public void InvokePreRequest(params object?[] args) {
        /*
        workspaceName = args[0]
        requestName = args[1]
        configHeaders = args[2]
        parameters = args[3]
        payload = args[4]
        cookies = args[5]
        extraArgs = args[6]
        */

        var workspaceName = args[0] as string ?? string.Empty;
        var requestName = args[1] as string ?? string.Empty;

        var workspace = _workspaceCache[workspaceName];
        var requests = workspace.requests as IDictionary<string, object>;
        var request = requests?[requestName] as dynamic;

        if (request is null) {
            // Create a minimal request shell so preRequest scripts that reference 'request' don't explode
            request = new ExpandoObject() as dynamic;
            request.name = requestName;
            request.headers = new ExpandoObject();
            request.parameters = new List<string>();
            request.payload = args[4] as string ?? string.Empty;
        }

        request.name = requestName;
        request.headers = new ExpandoObject() as dynamic;
        var headers = request.headers as IDictionary<string, object>;

        if (headers is null) {
            return;
        }

        var srcHeaders = args[2] as IDictionary<string, string>;

        // Copy headers from the caller into the dynamic request, overwriting duplicates
        if (srcHeaders is not null) {
            foreach (var kvp in srcHeaders) {
                headers[kvp.Key] = kvp.Value;
            }
        }

        var srcParameters = args[3] as IList<string>;
        var destParameters = request.parameters as IList<string>;

        if (srcParameters is not null && destParameters is not null) {
            foreach (var param in srcParameters) {
                destParameters.Add(param);
            }
        }

        var srcPayload = args[4] as string;
        request.payload = srcPayload;

        var extraArgs = args[6] as IEnumerable<object>;
        var invokeArgs = new List<object> { workspace, request };

        if (extraArgs is not null) {
            invokeArgs.AddRange(extraArgs);
        }

        try {
            // Ensure globals are present so script bodies can reference them directly
            Engine.Script.workspace = workspace;
            Engine.Script.request = request;
            // Lazily define request-level wrappers on first use
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var wsDef) && wsDef.Requests.TryGetValue(requestName, out var reqDef)) {
                EnsureRequestWrappers(workspaceName, wsDef, requestName, reqDef);
            }
            // If wrapper wasn't defined for some reason, fall back to workspace-level handler to avoid hard failure
            var exists = false;
            try { exists = (bool) (Engine.Evaluate($"typeof __preRequest__{workspaceName}__{requestName} === 'function'") ?? false); } catch { exists = false; }
            if (!exists) {
                if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:req] Missing __preRequest__{workspaceName}__{requestName}; falling back to __preRequest__{workspaceName}"); } catch { } }
                Engine.Invoke($"__preRequest__{workspaceName}", invokeArgs.ToArray());
            } else {
                var fn = $"__preRequest__{workspaceName}__{requestName}";
                var preRequestResult = Engine.Invoke(
                    fn,
                    invokeArgs.ToArray()
                    );
            }
        }
        catch (ScriptEngineException ex) {
            try { Console.Error.WriteLine($"[JS:err] PreRequest error: {ex.ErrorDetails}"); } catch { }
            throw new Exception(ex.ErrorDetails);
        }

        // Copy headers back to original dictionary
        // I think this can be done better.
        if (srcHeaders is not null && request.headers is not null) {
            foreach (var kvp in request.headers) {
                srcHeaders[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
            }
        }

        // Copy parameters back to original list
        if (srcParameters is not null && destParameters is not null) {
            srcParameters.Clear();
            foreach (var param in destParameters) {
                srcParameters.Add(param);
            }
        }

        srcPayload = request.payload;
    }

    public object? Invoke(string script, params object?[] args) {
        try {
            return Engine.Invoke(script, args);
        }
        catch (ScriptEngineException ex) {
            Console.Error.WriteLine(ex.ErrorDetails);
        }

        return null;
    }

    public object? InvokePostResponse(params object?[] args) {
        /*
        workspaceName = args[0]
        requestName = args[1]
        statusCode = args[2]
        headers = args[3]
        responseContent = args[4]
        extraArgs = args[5]
        */

        var workspaceName = args[0] as string ?? string.Empty;
        var requestName = args[1] as string ?? string.Empty;
        var statusCode = args[2] as int? ?? 0;
        var headers = args[3] as HttpResponseHeaders;
        var responseContent = args[4] as string ?? string.Empty;
        var extraArgs = args[5] as IEnumerable<object>;

        var workspace = _workspaceCache[workspaceName];
        var requests = workspace.requests as IDictionary<string, object>;

        if (requests is null) {
            return null;
        }

        var request = requests.ContainsKey(requestName) ? requests[requestName] as dynamic : null;

        if (request is null) {
            request = new ExpandoObject() as dynamic;
        }

        request.name = requestName;
        request.response = new ExpandoObject() as dynamic;
        request.response.statusCode = statusCode;
        request.response.headers = headers ?? default;
        request.response.body = responseContent;

        var invokeArgs = new List<object> { workspace, request };

        if (extraArgs is not null) {
            invokeArgs.AddRange(extraArgs);
        }

        try {
            // Lazily define request-level wrappers on first use
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var wsDef) && wsDef.Requests.TryGetValue(requestName, out var reqDef)) {
                EnsureRequestWrappers(workspaceName, wsDef, requestName, reqDef);
            }
            var exists = false;
            try { exists = (bool) (Engine.Evaluate($"typeof __postResponse__{workspaceName}__{requestName} === 'function'") ?? false); } catch { exists = false; }
            if (!exists) {
                if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:req] Missing __postResponse__{workspaceName}__{requestName}; falling back to __postResponse__{workspaceName}"); } catch { } }
                return Engine.Invoke($"__postResponse__{workspaceName}", invokeArgs.ToArray());
            } else {
                var fn = $"__postResponse__{workspaceName}__{requestName}";
                var postResponseResult = Engine.Invoke(
                    fn,
                    invokeArgs.ToArray()
                    );
                return postResponseResult;
            }
        }
        catch (ScriptEngineException ex) {
            try { Console.Error.WriteLine($"[JS:err] PostResponse error: {ex.ErrorDetails}"); } catch { }
            throw new Exception(ex.ErrorDetails);
        }
    }

    public void SetValue(string name, object? value) {
    Engine.AddHostObject(name, value);
    }

    public void SetResponse(ResponseDefinition dest, ResponseDefinition src) {
        dest.statusCode = src.statusCode;
        dest.body = src.body;
        dest.headers = src.headers;
    }

    public string ExecuteScript(string? script) {
        var scriptCode = GetScriptContent(script);

        if (string.IsNullOrEmpty(scriptCode)) {
            return string.Empty;
        }

    Engine.Execute(scriptCode);
        return string.Empty;
    }

    public object? EvaluateScript(string? script) {
        var scriptCode = GetScriptContent(script);

        if (string.IsNullOrEmpty(scriptCode)) {
            return string.Empty;
        }

    var result = Engine.Evaluate(scriptCode);

        if (result is null || result is Undefined || result is VoidResult) {
            return null;
        }

        return result;
    }

    public string ExecuteCommand(string? script) {
        return Engine.ExecuteCommand(script);
    }


    private string? GetScriptContent(string? scriptValue) {
        if (string.IsNullOrWhiteSpace(scriptValue)) {
            return string.Empty;
        }

        var originalDirectory = Directory.GetCurrentDirectory();
        var api2CliSettingsDirectory = _settingsService.Api2CliSettingsDirectory;

        try {
            scriptValue = scriptValue.ReplaceApi2CliPlaceholders(_propertyResolver, _settingsService);
            return scriptValue;
        }
        catch (Exception ex) {
            throw new Exception($"{Constants.ErrorChar} Error processing script content: {ex.Message}", ex);
        }
        finally {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    public void ExecuteInitScript(string? script) {
        if (string.IsNullOrWhiteSpace(script)) {
            return;
        }
        // Run at top-level; let exceptions surface to caller per ground rules
        ExecuteScript(script);
    }

    // Keyed init support (legacy): execute only when language is JavaScript; no parsing or transformation.
    public void ExecuteInitScript(XferKeyedValue? script) {
        var body = GetJsScriptBody(script);
        if (string.IsNullOrWhiteSpace(body)) {
            return; // Not JavaScript, or empty
        }
        ExecuteInitScript(body);
    }

    private static bool IsJavaScript(XferKeyedValue? kv) {
        var lang = kv?.Keys?.FirstOrDefault();
        return string.IsNullOrEmpty(lang)
            || ScriptEngineKinds.JavaScriptAliases.Contains(lang, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetJsScriptBody(XferKeyedValue? kv) {
        if (kv is null) { return string.Empty; }
        return IsJavaScript(kv) ? (kv.PayloadAsString ?? string.Empty) : string.Empty;
    }

    private void EnsureRequestWrappers(string workspaceName, WorkspaceDefinition workspace, string requestName, RequestDefinition requestDef) {
        var key = ReqKey(workspaceName, requestName);
        if (_definedRequestWrappers.Contains(key)) {
            return;
        }

        var argsBuilder = new StringBuilder();
        foreach (var arg in requestDef.Arguments) {
            argsBuilder.Append($", {arg.Key}");
        }
        var extraArgs = argsBuilder.ToString();

    try {
            Engine.Execute($@"
function __preRequest__{workspaceName}__{requestName} (workspace, request{extraArgs}) {{
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ __preRequest__{workspaceName}(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? ";" : $"__preRequest__{workspace.Extend}__{requestName}(workspace, request{extraArgs});")} }};
    let base = {{ preRequest: function() {{ baseHandler(); }}, postResponse: function() {{ return {(string.IsNullOrEmpty(workspace.Extend) ? "null" : $"__postResponse__{workspace.Extend}__{requestName}(workspace, request{extraArgs})")}; }} }};
    {((requestDef.PreRequest == null || !IsJavaScript(requestDef.PreRequest)) ? $"__preRequest__{workspaceName}(workspace, request)" : GetScriptContent(requestDef.PreRequest!.PayloadAsString))}
}}

function __postResponse__{workspaceName}__{requestName} (workspace, request{extraArgs}) {{
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ return __postResponse__{workspaceName}(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? "return null;" : $"return __postResponse__{workspace.Extend}__{requestName}(workspace, request{extraArgs});")} }};
    let base = {{ preRequest: function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? ";" : $"__preRequest__{workspace.Extend}__{requestName}(workspace, request{extraArgs});")} }}, postResponse: function() {{ return baseHandler(); }} }};
    {((requestDef.PostResponse == null || !IsJavaScript(requestDef.PostResponse)) ? $"return __postResponse__{workspaceName}(workspace, request)" : GetScriptContent(requestDef.PostResponse!.PayloadAsString))}
}}

");
            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:req] Defined wrappers for {workspaceName}.{requestName}"); } catch { } }
            _definedRequestWrappers.Add(key);
        }
        catch (ScriptEngineException ex) {
            Console.Error.WriteLine(ex.ErrorDetails);
        }
    }

}

public static class DynamicObjectExtensions {
    public static dynamic ToDynamic(object source) {
        if (source is null) {
            throw new ArgumentNullException(nameof(source));
        }

        IDictionary<string, object?> expando = new ExpandoObject();

        var properties = source.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        foreach (var prop in properties) {
            var value = prop.GetValue(source);
            expando[prop.Name] = value;
        }

        return (ExpandoObject) expando;
    }
}
