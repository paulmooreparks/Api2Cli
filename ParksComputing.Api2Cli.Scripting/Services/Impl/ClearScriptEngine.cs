using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

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
    private readonly HashSet<string> _definedWorkspaceWrappers = new(StringComparer.OrdinalIgnoreCase);
    // Cache compiled per-workspace init scripts so we don't reparse the same code repeatedly
    private readonly Dictionary<string, V8Script> _compiledWorkspaceInitScripts = new(StringComparer.OrdinalIgnoreCase);
    // Track global, workspace-agnostic init scripts we've already executed to avoid redundant runs across derived workspaces
    private readonly HashSet<string> _executedGlobalInits = new(StringComparer.OrdinalIgnoreCase);

    private static string ReqKey(string ws, string req) => ws + "|" + req;
    private static string JsEscape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

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

            // Optional timing for JS projection phase
            var timingsEnabled = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "1", StringComparison.OrdinalIgnoreCase);
            // Only emit engine-internal timing lines when detailed timings are requested to avoid duplicates
            var timingsDetail = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_DETAIL"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_DETAIL"), "1", StringComparison.OrdinalIgnoreCase);
            Stopwatch? swProject = null;
            if (timingsEnabled) {
                swProject = Stopwatch.StartNew();
            }
            // Optionally project only the active workspace and its base chain to reduce startup cost
            var projectActiveOnly = string.Equals(Environment.GetEnvironmentVariable("A2C_PROJECT_ACTIVE_ONLY"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_PROJECT_ACTIVE_ONLY"), "1", StringComparison.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (projectActiveOnly && _workspaceService?.BaseConfig is not null) {
                var baseConfig = _workspaceService.BaseConfig;
                var active = baseConfig.ActiveWorkspace;
                if (!string.IsNullOrWhiteSpace(active) && baseConfig.Workspaces.TryGetValue(active!, out var activeWs)) {
                    // include active and walk base chain via Extend
                    string cur = active!;
                    WorkspaceDefinition? curDef = activeWs;
                    while (!string.IsNullOrEmpty(cur) && curDef is not null) {
                        allowed.Add(cur);
                        if (!string.IsNullOrWhiteSpace(curDef.Extend) && baseConfig.Workspaces.TryGetValue(curDef.Extend, out var baseWs)) {
                            cur = curDef.Extend;
                            curDef = baseWs;
                        } else {
                            break;
                        }
                    }
                }
            }

            // Accumulate all request execute/exec shims across workspaces and execute once at the end
            var shimGlobalBuilder = new StringBuilder();
            if (_workspaceService?.BaseConfig is null) { return; }
            var bc = _workspaceService.BaseConfig;
            foreach (var workspaceKvp in bc.Workspaces) {
                if (projectActiveOnly && allowed.Count > 0 && !allowed.Contains(workspaceKvp.Key)) {
                    continue;
                }
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

                // Defer defining workspace-level wrappers until first use to reduce startup cost.

                // Populate request shims within workspaceKvp without constructing heavy C# objects
                // Batch JS shim creation per workspace to avoid many Engine.Execute calls
                var shimBuilder = new StringBuilder();
                var wsEscaped = JsEscape(workspaceName);
                // Open a single IIFE for this workspace; pre-fetch the workspace object in JS
                shimBuilder.Append($"(function(){{ var ws = a2c.workspaces && a2c.workspaces['{wsEscaped}']; if (!ws) return; ");

                foreach (var request in workspace.Requests) {
                    var requestName = request.Key;
                    // Defer defining per-request wrappers to first use for faster startup
                    // Create a lightweight placeholder and execute/exec shims that call back into host
                    var reqEsc = JsEscape(requestName);
                    shimBuilder.Append($@"(function(){{
    if (!ws.requests) ws.requests = {{}};
    var req = ws.requests['{reqEsc}'];
    if (!req) {{
        req = {{}};
        ws.requests['{reqEsc}'] = req;
        if (!ws['{reqEsc}']) ws['{reqEsc}'] = req;
    }}
    if (typeof req.execute !== 'function') {{
        req.execute = function() {{
            var args = Array.prototype.slice.call(arguments);
            if (typeof a2cRequestInvoke === 'function') return a2cRequestInvoke('{wsEscaped}', '{reqEsc}', args);
            throw new Error('a2cRequestInvoke not available');
        }};
    }}
    if (typeof req.exec !== 'function') {{ req.exec = req.execute; }}
}})();");
                }

                // Close and append the per-workspace shim batch to the global buffer (execute once after loop)
                shimBuilder.Append("})();");
                shimGlobalBuilder.Append(shimBuilder.ToString());

            }

            // Execute all generated shims in a single engine call to minimize overhead
            if (shimGlobalBuilder.Length > 0) {
                try { Engine.Execute(shimGlobalBuilder.ToString()); }
                catch (ScriptEngineException ex) { Console.Error.WriteLine(ex.ErrorDetails); }
            }

            if (timingsEnabled && timingsDetail && swProject is not null) {
                var line = $"A2C_TIMINGS: jsProject={swProject.Elapsed.TotalMilliseconds:F1} ms";
                var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
                try { Console.WriteLine(line); } catch { }
                if (mirror) { try { Console.Error.WriteLine(line); } catch { } }
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
    var timingsEnabled = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "1", StringComparison.OrdinalIgnoreCase);
    var timingsDetail = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_DETAIL"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_DETAIL"), "1", StringComparison.OrdinalIgnoreCase);
    Stopwatch? swWsInit = null;
    if (timingsEnabled) { swWsInit = Stopwatch.StartNew(); }
    // If requested, limit per-workspace JS init to the active workspace and its base chain
    var projectActiveOnly = string.Equals(Environment.GetEnvironmentVariable("A2C_PROJECT_ACTIVE_ONLY"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("A2C_PROJECT_ACTIVE_ONLY"), "1", StringComparison.OrdinalIgnoreCase);
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (projectActiveOnly && _workspaceService?.BaseConfig is not null) {
        var baseConfig = _workspaceService.BaseConfig;
        var active = baseConfig.ActiveWorkspace;
        if (!string.IsNullOrWhiteSpace(active) && baseConfig.Workspaces.TryGetValue(active!, out var activeWs)) {
            string cur = active!;
            WorkspaceDefinition? curDef = activeWs;
            while (!string.IsNullOrEmpty(cur) && curDef is not null) {
                allowed.Add(cur);
                if (!string.IsNullOrWhiteSpace(curDef.Extend) && baseConfig.Workspaces.TryGetValue(curDef.Extend, out var baseWs)) {
                    cur = curDef.Extend;
                    curDef = baseWs;
                } else {
                    break;
                }
            }
        }
    }
    // Optional limiter to reduce startup time in very large configs (primarily for debugging/tuning)
    int limit = int.MaxValue;
    var limitEnv = Environment.GetEnvironmentVariable("A2C_WS_INIT_LIMIT");
    if (!string.IsNullOrWhiteSpace(limitEnv) && int.TryParse(limitEnv, out var parsed) && parsed > 0) {
        limit = parsed;
    }
    int count = 0;
    var bc = _workspaceService!.BaseConfig;
    if (bc?.Workspaces is null) { return; }
    foreach (var wsInit in bc.Workspaces) {
            var wsName = wsInit.Key;
            var wsDef = wsInit.Value;
            if (projectActiveOnly && allowed.Count > 0 && !allowed.Contains(wsName)) { continue; }
            if (_workspaceCache.TryGetValue(wsName, out var wsObj)) {
        if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Execute per-workspace init (grouped) -> {wsName}"); } catch { } }
                ExecuteWorkspaceInitChain(wsName, wsDef, wsObj);
        if (++count >= limit) { break; }
            }
        }
    if (ScriptDebug) { try { Console.Error.WriteLine("[JS:init] ExecuteAllWorkspaceInitScripts end"); } catch { } }
    if (timingsEnabled && timingsDetail && swWsInit is not null) {
        var line = $"A2C_TIMINGS: jsWorkspaceInit={swWsInit.Elapsed.TotalMilliseconds:F1} ms";
        var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
        try { Console.WriteLine(line); } catch { }
        if (mirror) { try { Console.Error.WriteLine(line); } catch { } }
    }
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
                    // Compile the init script once per workspace key; reuse across derived workspaces.
                    if (!_compiledWorkspaceInitScripts.TryGetValue(workspaceKey, out var compiled)) {
                        // Note: script runs at top-level so function/var declarations persist globally.
                        // It will reference the global 'workspace' variable which we bind before execution.
                        compiled = Engine.Compile(new DocumentInfo($"wsinit:{workspaceKey}"), scriptCode);
                        _compiledWorkspaceInitScripts[workspaceKey] = compiled;
                    }
                    // If script is workspace-agnostic (doesn't reference 'workspace' or 'request'), execute only once globally
                    if (!IsWorkspaceDependent(scriptCode)) {
                        if (_executedGlobalInits.Contains(workspaceKey)) {
                            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Skipping redundant global init for '{workspaceKey}'"); } catch { } }
                            return;
                        }
                        if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Running global init once for '{workspaceKey}'"); } catch { } }
                        Engine.Execute(_compiledWorkspaceInitScripts[workspaceKey]);
                        _executedGlobalInits.Add(workspaceKey);
                    } else {
                        // Bind the target workspace object and execute compiled script.
                        try { Engine.Script.workspace = workspaceObj; } catch { }
                        if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:init] Running init for workspace '{workspaceKey}'"); } catch { } }
                        Engine.Execute(_compiledWorkspaceInitScripts[workspaceKey]);
                    }
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
            // Lazily define workspace-level wrappers if missing
            bool wsWrappersExist = false;
            try { wsWrappersExist = (bool)(Engine.Evaluate($"typeof __preRequest__{workspaceName} === 'function'") ?? false); } catch { wsWrappersExist = false; }
            if (!wsWrappersExist && _workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var wsDefForWrappers)) {
                EnsureWorkspaceWrappers(workspaceName, wsDefForWrappers);
            }
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
            // Ensure workspace-level wrappers exist
            bool wsWrappersExist = false;
            try { wsWrappersExist = (bool)(Engine.Evaluate($"typeof __postResponse__{workspaceName} === 'function'") ?? false); } catch { wsWrappersExist = false; }
            if (!wsWrappersExist && _workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var wsDefForWrappers)) {
                EnsureWorkspaceWrappers(workspaceName, wsDefForWrappers);
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

    private void EnsureWorkspaceWrappers(string workspaceName, WorkspaceDefinition workspace)
    {
        if (_definedWorkspaceWrappers.Contains(workspaceName)) {
            return;
        }
        try {
            Engine.Execute($@"
function __preRequest__{workspaceName}(workspace, request) {{
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ __preRequest(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? "" : $"__preRequest__{workspace.Extend}(workspace, request);")} }};
    let base = {{ preRequest: function() {{ baseHandler(); }}, postResponse: function() {{ return {(string.IsNullOrEmpty(workspace.Extend) ? "null" : $"__postResponse__{workspace.Extend}(workspace, request)")}; }} }};
    {((workspace.PreRequest == null || !IsJavaScript(workspace.PreRequest)) ? $"__preRequest(workspace, request)" : GetScriptContent(workspace.PreRequest!.PayloadAsString))}
}};

function __postResponse__{workspaceName}(workspace, request) {{
    try {{ this.workspace = workspace; this.request = request; }} catch (e) {{}}
    let nextHandler = function() {{ return __postResponse(workspace, request); }};
    let baseHandler = function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? "return null;" : $"return __postResponse__{workspace.Extend}(workspace, request);")} }};
    let base = {{ preRequest: function() {{ {(string.IsNullOrEmpty(workspace.Extend) ? ";" : $"__preRequest__{workspace.Extend}(workspace, request);")} }}, postResponse: function() {{ return baseHandler(); }} }};
    {((workspace.PostResponse == null || !IsJavaScript(workspace.PostResponse)) ? $"return __postResponse(workspace, request)" : GetScriptContent(workspace.PostResponse!.PayloadAsString))}
}};

");
            if (ScriptDebug) { try { Console.Error.WriteLine($"[JS:req] Defined workspace wrappers for {workspaceName}"); } catch { } }
            _definedWorkspaceWrappers.Add(workspaceName);
        }
        catch (ScriptEngineException ex) {
            Console.Error.WriteLine(ex.ErrorDetails);
        }
    }

    private static bool IsWorkspaceDependent(string? scriptCode)
    {
    if (string.IsNullOrWhiteSpace(scriptCode)) { return false; }
        return scriptCode.IndexOf("workspace", StringComparison.OrdinalIgnoreCase) >= 0
            || scriptCode.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Project a single workspace object and its request shims into the JS engine on demand.
    public void EnsureWorkspaceProjected(string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName)) { return; }
        if (_workspaceCache.ContainsKey(workspaceName)) { return; }
        var bc = _workspaceService?.BaseConfig;
        if (bc?.Workspaces is null) { return; }
        if (!bc.Workspaces.TryGetValue(workspaceName, out var workspace) || workspace is null) { return; }

        // Link base definition if available
        if (!string.IsNullOrEmpty(workspace.Extend) && bc.Workspaces.TryGetValue(workspace.Extend, out var baseWorkspace)) {
            workspace.Base = baseWorkspace;
        }

        // Create dynamic workspace object mirroring InitializeScriptEnvironment
        var workspaceObj = new ExpandoObject() as dynamic;
        workspaceObj.name = workspace.Name ?? workspaceName;
        workspaceObj.extend = workspace.Extend;
        workspaceObj.baseWorkspace = workspace.Base;
        workspaceObj.baseUrl = workspace.BaseUrl; // already placeholder-expanded by callers on use
        workspaceObj.requests = new ExpandoObject() as dynamic;

        // Copy arbitrary workspace properties
        var workspaceDict = (IDictionary<string, object>)workspaceObj;
        foreach (var kvp in workspace.Properties) {
            if (!workspaceDict.ContainsKey(kvp.Key)) {
                workspaceDict.Add(kvp.Key, kvp.Value);
            } else {
                _diags.Emit(nameof(ClearScriptEngine), new { Message = $"Failed to set property {kvp.Key} to {kvp.Value} in workspace {workspaceName}" });
            }
        }

        _workspaceCache[workspaceName] = workspaceObj;
        (_a2c.Workspaces as IDictionary<string, object?>)!.Add(workspaceName, workspaceObj);
        _a2c[workspaceName] = workspaceObj;

        // Define lightweight execute/exec shims for requests under this workspace
        if (workspace.Requests is not null && workspace.Requests.Count > 0) {
            var wsEscaped = JsEscape(workspaceName);
            var shimBuilder = new StringBuilder();
            shimBuilder.Append($"(function(){{ var ws = a2c.workspaces && a2c.workspaces['{wsEscaped}']; if (!ws) return; ");
            foreach (var req in workspace.Requests) {
                var reqEsc = JsEscape(req.Key);
                shimBuilder.Append($@"(function(){{
    if (!ws.requests) ws.requests = {{}};
    var r = ws.requests['{reqEsc}'];
    if (!r) {{ r = {{}}; ws.requests['{reqEsc}'] = r; if (!ws['{reqEsc}']) ws['{reqEsc}'] = r; }}
    if (typeof r.execute !== 'function') {{
        r.execute = function() {{
            var args = Array.prototype.slice.call(arguments);
            if (typeof a2cRequestInvoke === 'function') return a2cRequestInvoke('{wsEscaped}', '{reqEsc}', args);
            throw new Error('a2cRequestInvoke not available');
        }};
    }}
    if (typeof r.exec !== 'function') {{ r.exec = r.execute; }}
}})();");
            }
            shimBuilder.Append("})();");
            try { Engine.Execute(shimBuilder.ToString()); }
            catch (ScriptEngineException ex) { Console.Error.WriteLine(ex.ErrorDetails); }
        }
    }

    // Execute init scripts for a single workspace (base-first), compiling as needed.
    public void ExecuteWorkspaceInitFor(string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName)) { return; }
        var bc = _workspaceService?.BaseConfig;
        if (bc?.Workspaces is null) { return; }
        if (!bc.Workspaces.TryGetValue(workspaceName, out var wsDef) || wsDef is null) { return; }
        if (!_workspaceCache.TryGetValue(workspaceName, out var wsObj)) {
            // Project on demand if not present yet
            EnsureWorkspaceProjected(workspaceName);
            if (!_workspaceCache.TryGetValue(workspaceName, out wsObj)) { return; }
        }
        ExecuteWorkspaceInitChain(workspaceName, wsDef, wsObj);
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
