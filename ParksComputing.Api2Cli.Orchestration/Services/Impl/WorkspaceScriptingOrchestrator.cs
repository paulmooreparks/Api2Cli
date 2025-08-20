using System;
using System.Collections.Generic;
using System.Linq;

using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Xfer.Lang;
using ParksComputing.Api2Cli.Workspace.Models;
using System.Net.Http.Headers;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using static ParksComputing.Api2Cli.Scripting.Services.ScriptEngineKinds;
// Note: Do not depend on CLI layer here.

namespace ParksComputing.Api2Cli.Orchestration.Services.Impl;

internal partial class WorkspaceScriptingOrchestrator : IWorkspaceScriptingOrchestrator {
    private readonly IApi2CliScriptEngineFactory _engineFactory;
    private readonly IWorkspaceService _workspaceService;
    private Func<string, string, object?[]?, object?>? _requestExecutor;
    // Removed host-side type conversion to streamline startup; no param signature caching
    // Lazy C# handler-chain state
    private bool _needCs = false;
    private bool _hasAnyCSharpHandlers = false;
    private bool _csGlobalBuilt = false;
    private readonly HashSet<string> _csWorkspaceBuilt = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _csBuildLock = new();
    // Lazy C# wrapper compilation for scripts
    private readonly HashSet<string> _csCompiledWrappers = new(StringComparer.OrdinalIgnoreCase);
    private sealed class CsWrapperDef { public string Body = string.Empty; public List<(string TypeToken, string Name)> Args = new(); public bool HasWorkspace; }
    private readonly Dictionary<string, CsWrapperDef> _csWrappers = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceScriptingOrchestrator(IApi2CliScriptEngineFactory engineFactory, IWorkspaceService workspaceService) {
        _engineFactory = engineFactory;
        _workspaceService = workspaceService;
    }

    // Debug gate: use A2C_SCRIPT_DEBUG env var ("true" or "1") to enable extra logs
    private static bool IsScriptDebugEnabled() {
        var v = Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG");
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "1", StringComparison.OrdinalIgnoreCase);
    }

    public void RegisterRequestExecutor(Func<string, string, object?[]?, object?> executor) {
        _requestExecutor = executor ?? throw new ArgumentNullException(nameof(executor));

        // Wire the host-side request invoker and ensure JS execute() shims exist for all requests
        var js = _engineFactory.GetEngine(JavaScript);
        // Accept a flexible args object from JS and normalize to object?[] to avoid ClearScript marshaling issues
        js.AddHostObject("a2cRequestInvoke", new Func<string, string, object?, object?>((w, r, argsObj) => {
            object?[] argsArray = argsObj switch {
                null => Array.Empty<object?>(),
                object?[] arr => arr,
                System.Collections.IEnumerable e when argsObj is not string => e.Cast<object?>().ToArray(),
                _ => new object?[] { argsObj }
            };
            return _requestExecutor!(w, r, argsArray);
        }));

    // No per-request JS wiring here; ClearScriptEngine defines minimal execute() shims lazily per request
    }

    public void Initialize() {
        var js = _engineFactory.GetEngine(JavaScript);
        js.InitializeScriptEnvironment();

        var baseConfig = _workspaceService.BaseConfig;

        // Detect whether any C# features are present to avoid spinning up Roslyn unless needed
        bool anyCsScripts = baseConfig.Scripts.Any(s => string.Equals(s.Value.ScriptTags?.FirstOrDefault() ?? JavaScript, CSharp, StringComparison.OrdinalIgnoreCase))
            || baseConfig.Workspaces.Any(ws => ws.Value.Scripts.Any(s => string.Equals(s.Value.ScriptTags?.FirstOrDefault() ?? JavaScript, CSharp, StringComparison.OrdinalIgnoreCase)));
        bool anyCsInit = (baseConfig.ScriptInit?.CSharp is string csInit && !string.IsNullOrWhiteSpace(csInit))
            || IsCSharp(baseConfig.InitScript)
            || baseConfig.Workspaces.Any(ws => (ws.Value.ScriptInit?.CSharp is string wcs && !string.IsNullOrWhiteSpace(wcs)) || IsCSharp(ws.Value.InitScript));
        bool anyCsHandlers = IsCSharp(baseConfig.PreRequest) || IsCSharp(baseConfig.PostResponse)
            || baseConfig.Workspaces.Any(w => IsCSharp(w.Value.PreRequest) || IsCSharp(w.Value.PostResponse) || w.Value.Requests.Any(r => IsCSharp(r.Value.PreRequest) || IsCSharp(r.Value.PostResponse)));

    bool needCs = anyCsScripts || anyCsInit || anyCsHandlers;
    _needCs = needCs;
    _hasAnyCSharpHandlers = anyCsHandlers;

    IApi2CliScriptEngine? cs = null;
        if (needCs) {
            cs = _engineFactory.GetEngine(CSharp);
            cs.InitializeScriptEnvironment();
        }

        // CLI-level JS function cache is process-local; nothing to clear here.

    if (needCs) {
            // Bridge for invoking C# script wrappers from JS with host-side argument conversion
            var csRef = cs!;
            js.AddHostObject("a2cCsharpInvoke", new Func<string, object?, object?>((fname, argsObj) => {
                object?[] argsArray = argsObj switch {
                    null => Array.Empty<object?>(),
                    object?[] arr => arr,
                    System.Collections.IEnumerable e when argsObj is not string => e.Cast<object?>().ToArray(),
                    _ => new object?[] { argsObj }
                };
                if (!_csCompiledWrappers.Contains(fname)) {
                    CompileCSharpWrapper(csRef, fname);
                }
                // Perform minimal, strict conversions for declared typed arguments
                if (_csWrappers.TryGetValue(fname, out var def)) {
                    try {
                        argsArray = ConvertArgsForWrapper(argsArray, def);
                    } catch (Exception ex) {
                        // Surface conversion problems clearly to the script side
                        throw new InvalidOperationException($"Failed to convert arguments for '{fname}': {ex.Message}", ex);
                    }
                }
                return csRef.Invoke(fname, argsArray);
            }));
            js.EvaluateScript("a2c.csharpInvoke = function(name, args) { return a2cCsharpInvoke(name, args); };");
        }

        // Execute new global scriptInit if present: C# first, then JavaScript
    if (baseConfig.ScriptInit is not null) {
            if (needCs && !string.IsNullOrWhiteSpace(baseConfig.ScriptInit.CSharp)) {
        if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] Global C# scriptInit begin"); } catch { } }
                cs!.ExecuteInitScript(baseConfig.ScriptInit.CSharp);
        if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] Global C# scriptInit end"); } catch { } }
            }
            if (!string.IsNullOrWhiteSpace(baseConfig.ScriptInit.JavaScript)) {
                js.ExecuteInitScript(baseConfig.ScriptInit.JavaScript);
            }
            // Now that global init ran, execute per-workspace JavaScript init (base-first) via JS engine helper
            try {
                var jsImpl = _engineFactory.GetEngine(JavaScript);
                // Invoke through the engine interface (no-op for non-JS engines)
                jsImpl.ExecuteAllWorkspaceInitScripts();
            } catch { /* ignore; best-effort */ }
        }

        // Execute per-workspace C# scriptInit when provided (base-first), else run legacy C# init chain
        if (needCs) {
            if (baseConfig.ScriptInit is null) {
                if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] ExecuteCSharpInitScripts begin"); } catch { } }
                ExecuteCSharpInitScripts();
                if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] ExecuteCSharpInitScripts end"); } catch { } }
            }
            else {
                ExecuteAllWorkspaceCSharpScriptInits();
            }
        }

        // Note: Per-workspace JavaScript init is executed inside the JS engine initialization
        // (ClearScriptEngine.InitializeScriptEnvironment) to ensure base-first ordering and workspace binding.

    // Register root scripts into JS engine's a2c and optionally C# engine globals.
    // Batch JS registrations to minimize host transitions.
    bool dbg = string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
    if (dbg) { try { System.Console.Error.WriteLine("[Orch] Register root scripts begin"); } catch { } }
        var jsRootBlock = new System.Text.StringBuilder();
        var jsRootCSharpShims = new System.Text.StringBuilder();
        foreach (var script in _workspaceService.BaseConfig.Scripts) {
            var name = script.Key;
            var def = script.Value;
            var (lang, body) = def.ResolveLanguageAndBody();
            var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));

            if (string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                // Define as a property assignment with an anonymous function to avoid polluting global scope
                jsRootBlock.Append($"a2c['{name}'] = function({paramList}) {{\n{body}\n}};\n");
            }
            else if (needCs && string.Equals(lang, CSharp, StringComparison.OrdinalIgnoreCase)) {
                // Record wrapper definition for lazy compile and set up param signatures
                var key = $"__script__{name}";
                _csWrappers[key] = new CsWrapperDef {
                    Body = body ?? string.Empty,
                    HasWorkspace = false,
                    Args = def.Arguments.Select(a => (ResolveCSharpTypeToken(a.Value.Type), a.Value.Name ?? a.Key)).ToList()
                };
                // JS shim to call C# wrapper (host will convert types and compile lazily)
                jsRootCSharpShims.Append($"a2c['{name}'] = (...args) => a2c.csharpInvoke('{key}', args);\n");
            }
        }
        if (jsRootBlock.Length > 0) { js.EvaluateScript(jsRootBlock.ToString()); }
        if (jsRootCSharpShims.Length > 0) { js.EvaluateScript(jsRootCSharpShims.ToString()); }
        if (dbg) { try { System.Console.Error.WriteLine("[Orch] Register root scripts end"); } catch { } }

        // Register workspace scripts wrappers for both JS and C#; assume a2c.workspaces exists
        if (dbg) { try { System.Console.Error.WriteLine("[Orch] Register workspace scripts begin"); } catch { } }
        if (_requestExecutor is not null) {
            // Add a single bridge function once (robust args normalization)
            js.AddHostObject("a2cRequestInvoke", new Func<string, string, object?, object?>((w, r, argsObj) => {
                object?[] argsArray = argsObj switch {
                    null => Array.Empty<object?>(),
                    object?[] arr => arr,
                    System.Collections.IEnumerable e when argsObj is not string => e.Cast<object?>().ToArray(),
                    _ => new object?[] { argsObj }
                };
                return _requestExecutor!(w, r, argsArray);
            }));
        }
        foreach (var wkvp in _workspaceService.BaseConfig.Workspaces) {
            var wsName = wkvp.Key;
            var ws = wkvp.Value;
            if (dbg) { try { System.Console.Error.WriteLine($"[Orch] Workspace '{wsName}': scripts={ws.Scripts.Count}, requests={ws.Requests.Count}"); } catch { } }
            var jsWsBlock = new System.Text.StringBuilder();
            var jsWsCSharpShims = new System.Text.StringBuilder();
            foreach (var script in ws.Scripts) {
                var name = script.Key;
                var def = script.Value;
                var (lang, body) = def.ResolveLanguageAndBody();
                var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));
                if (string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                    // Assign a closure that captures workspace as a hidden parameter; avoid global function pollution
                    jsWsBlock.Append($"a2c.workspaces['{wsName}']['{name}'] = (function(workspace) {{ return function({paramList}) {{\n{body}\n}}; }})(a2c.workspaces['{wsName}']);\n");
                }
                else if (needCs && string.Equals(lang, CSharp, StringComparison.OrdinalIgnoreCase)) {
                    // Record workspace wrapper for lazy compile. First hidden param is workspace.
                    var key = $"__script__{wsName}__{name}";
                    _csWrappers[key] = new CsWrapperDef {
                        Body = body ?? string.Empty,
                        HasWorkspace = true,
                        Args = def.Arguments.Select(a => (ResolveCSharpTypeToken(a.Value.Type), a.Value.Name ?? a.Key)).ToList()
                    };
                    // JS shim injects workspace as first arg; host will compile lazily
                    jsWsCSharpShims.Append($"a2c.workspaces['{wsName}']['{name}'] = (...args) => a2c.csharpInvoke('{key}', [a2c.workspaces['{wsName}'], ...args]);\n");
                }
            }
            if (jsWsBlock.Length > 0) { js.EvaluateScript(jsWsBlock.ToString()); }
            if (jsWsCSharpShims.Length > 0) { js.EvaluateScript(jsWsCSharpShims.ToString()); }

            // No per-request wiring here; let the JS engine's lazy shims call a2cRequestInvoke when present
        }
        if (dbg) { try { System.Console.Error.WriteLine("[Orch] Register workspace scripts end"); } catch { } }

        // C# init already executed above to satisfy wrapper dependencies

    // C# handler classes will be built lazily per workspace on first use
    }

    // Lazily compile a recorded C# wrapper into a Func<...> and register into the C# engine under the given key
    private void CompileCSharpWrapper(IApi2CliScriptEngine cs, string key) {
        if (_csCompiledWrappers.Contains(key)) {
            return;
        }
        if (!_csWrappers.TryGetValue(key, out var def)) {
            // Nothing recorded; avoid repeated lookups
            _csCompiledWrappers.Add(key);
            return;
        }
        // Build generic signature and parameter list
        string funcGeneric;
        string paramSig;
        if (def.HasWorkspace) {
            funcGeneric = "object" + (def.Args.Count > 0 ? ", " + string.Join(", ", def.Args.Select(a => a.TypeToken)) + ", object" : ", object");
            paramSig = "dynamic workspace" + (def.Args.Count > 0 ? ", " + string.Join(", ", def.Args.Select(a => $"{a.TypeToken} {a.Name}")) : string.Empty);
        }
        else {
            funcGeneric = def.Args.Count > 0 ? string.Join(", ", def.Args.Select(a => a.TypeToken)) + ", object" : "object";
            paramSig = def.Args.Count > 0 ? string.Join(", ", def.Args.Select(a => $"{a.TypeToken} {a.Name}")) : string.Empty;
        }
        var body = EnsureReturns(def.Body);
        var code = $@"new System.Func<{funcGeneric}>(({paramSig}) => {{
    {body}
}})";
        var compiled = cs.EvaluateScript(code);
        cs.SetValue(key, compiled);
        _csCompiledWrappers.Add(key);
    }

    public void InvokePreRequest(
        string workspaceName,
        string requestName,
        IDictionary<string, string> headers,
        IList<string> parameters,
        ref string? payload,
        IDictionary<string, string> cookies,
        object?[] extraArgs
    ) {
        var baseConfig = _workspaceService.BaseConfig;
        var js = _engineFactory.GetEngine(JavaScript);

        if (_needCs && _hasAnyCSharpHandlers) {
            var cs = _engineFactory.GetEngine(CSharp);
            EnsureCSharpHandlersFor(workspaceName);
            // Prepare call inputs for typed C# delegate wrappers
            dynamic payloadBox = new System.Dynamic.ExpandoObject();
            payloadBox.Value = payload;
            var wsDyn = new System.Dynamic.ExpandoObject();
            ((IDictionary<string, object?>) wsDyn)["name"] = workspaceName;
            var reqDyn = new System.Dynamic.ExpandoObject();
            ((IDictionary<string, object?>) reqDyn)["name"] = requestName;
            ((IDictionary<string, object?>) reqDyn)["headers"] = headers;
            ((IDictionary<string, object?>) reqDyn)["parameters"] = parameters;
            ((IDictionary<string, object?>) reqDyn)["payload"] = payload;

            // Invoke only the most-derived C# preRequest delegate; base chaining is handled inside the class
            var reqKey = $"__cs_pre__{Sanitize(workspaceName)}__{Sanitize(requestName)}";
            object?[] callArgs = new object?[] { wsDyn, reqDyn, headers, parameters, payloadBox, cookies, extraArgs };
            cs.Invoke(reqKey, callArgs);

            // Apply any payload change made by C# scripts via payloadBox
            if (payloadBox is IDictionary<string, object?> dict && dict.TryGetValue("Value", out var pv)) {
                payload = pv as string;
            }
        }

        // Defer to existing JS preRequest wrapper chain for JavaScript handlers
        js.InvokePreRequest(workspaceName, requestName, headers, parameters, payload, cookies, extraArgs);
    }

    public object? InvokePostResponse(
        string workspaceName,
        string requestName,
        int statusCode,
        System.Net.Http.Headers.HttpResponseHeaders headers,
        string responseContent,
        object?[] extraArgs
    ) {
        var js = _engineFactory.GetEngine(JavaScript);
        object? lastCsResult = null;

        if (_needCs && _hasAnyCSharpHandlers) {
            var cs = _engineFactory.GetEngine(CSharp);
            EnsureCSharpHandlersFor(workspaceName);

            // Prepare inputs and invoke most-derived C# postResponse delegate
            var wsDyn2 = new System.Dynamic.ExpandoObject();
            ((IDictionary<string, object?>) wsDyn2)["name"] = workspaceName;
            var reqDyn2 = new System.Dynamic.ExpandoObject();
            ((IDictionary<string, object?>) reqDyn2)["name"] = requestName;
            var respObj = new System.Dynamic.ExpandoObject();
            ((IDictionary<string, object?>) respObj)["statusCode"] = statusCode;
            ((IDictionary<string, object?>) respObj)["headers"] = headers;
            ((IDictionary<string, object?>) respObj)["body"] = responseContent;
            ((IDictionary<string, object?>) reqDyn2)["response"] = respObj;

            var reqKey2 = $"__cs_post__{Sanitize(workspaceName)}__{Sanitize(requestName)}";
            var wsKey2 = $"__cs_post__{Sanitize(workspaceName)}";
            var globalKey2 = "__cs_post__global";
            object?[] postArgs = new object?[] { wsDyn2, reqDyn2, statusCode, headers, responseContent, extraArgs };
            lastCsResult = cs.Invoke(reqKey2, postArgs);
            if (lastCsResult is null || (lastCsResult is string sres && string.Equals(sres, responseContent, StringComparison.Ordinal))) {
                lastCsResult = cs.Invoke(wsKey2, postArgs) ?? cs.Invoke(globalKey2, postArgs);
            }
        }

        // Then defer to JS chain; prefer JS result only when it actually changes the content
        var jsResult = js.InvokePostResponse(workspaceName, requestName, statusCode, headers, responseContent, extraArgs);
        if (jsResult is null) {
            return lastCsResult;
        }
        if (jsResult is string jsStr && string.Equals(jsStr, responseContent, StringComparison.Ordinal)) {
            // JS chain returned original content (no-op); favor C# result if it changed content
            if (lastCsResult is string csStr && !string.Equals(csStr, responseContent, StringComparison.Ordinal)) {
                return csStr;
            }
        }
        return jsResult ?? lastCsResult;
    }

    // Build global and specified workspace C# handlers on first use
    private void EnsureCSharpHandlersFor(string workspaceName) {
        if (!_needCs || !_hasAnyCSharpHandlers)
        {
            return;
        }
        lock (_csBuildLock) {
            var cs = _engineFactory.GetEngine(CSharp);
            if (!_csGlobalBuilt) {
                BuildGlobalHandlers(cs);
                _csGlobalBuilt = true;
            }
            var safeWs = Sanitize(workspaceName);
            if (!_csWorkspaceBuilt.Contains(safeWs)) {
                if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var ws)) {
                    BuildWorkspaceHandlerRecursive(cs, workspaceName, ws);
                    _csWorkspaceBuilt.Add(safeWs);
                }
            }
        }
    }

    private void BuildGlobalHandlers(IApi2CliScriptEngine cs) {
        var baseConfig = _workspaceService.BaseConfig;
        var globalPreBody = IsCSharp(baseConfig.PreRequest) ? baseConfig.PreRequest!.PayloadAsString ?? string.Empty : string.Empty;
        var globalPostBody = IsCSharp(baseConfig.PostResponse) ? baseConfig.PostResponse!.PayloadAsString ?? string.Empty : "return ResponseContent;";
        var globalCode = $@"public class __CsHandlers_Global {{
    public virtual void PreRequest(dynamic workspace, dynamic request, System.Collections.Generic.IDictionary<string,string> Headers, System.Collections.Generic.IList<string> Parameters, dynamic PayloadBox, System.Collections.Generic.IDictionary<string,string> Cookies, params object?[] ExtraArgs) {{
        {globalPreBody}
    }}
    public virtual object? PostResponse(dynamic workspace, dynamic request, int StatusCode, System.Net.Http.Headers.HttpResponseHeaders ResponseHeaders, string ResponseContent, params object?[] ExtraArgs) {{
        {globalPostBody}
    }}
}}
new __CsHandlers_Global()
";
        var gHandler = cs.EvaluateScript(globalCode);
        cs.SetValue("__cs_pre__global", (Action<dynamic, dynamic, IDictionary<string, string>, IList<string>, dynamic, IDictionary<string, string>, object?[]>) ((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
            var h = gHandler; if (h == null) { return; }
            ((dynamic) h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
        }));
        cs.SetValue("__cs_post__global", (Func<dynamic, dynamic, int, HttpResponseHeaders, string, object?[], object>) ((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
            var h = gHandler; if (h == null) { return ResponseContent; }
            return ((dynamic) h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
        }));
    }

    private static bool IsCSharp(XferKeyedValue? kv) {
        var lang = kv?.Keys?.FirstOrDefault();
        return ScriptEngineKinds.CSharpAliases.Contains(lang ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public void Warmup(int limit = 25, bool enable = false, bool debug = false) {
        if (!enable) {
            return;
        }
        int warmed = 0;
        var js = _engineFactory.GetEngine(JavaScript);

        // Touch a limited number of JS script references to ensure they are defined
        foreach (var kvp in _workspaceService.BaseConfig.Scripts) {
            if (warmed >= limit) {
                break;
            }
            var def = kvp.Value;
            var lang = def.ScriptTags?.FirstOrDefault() ?? JavaScript;

            if (!ScriptEngineKinds.JavaScriptAliases.Contains(lang, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            try { js.EvaluateScript($"void(a2c['{kvp.Key}'])"); } catch { /* ignore */ }
            warmed++;
        }
        if (warmed < limit) {
            foreach (var wkvp in _workspaceService.BaseConfig.Workspaces) {
                if (warmed >= limit) {
                    break;
                }

                var wsName = wkvp.Key;
                foreach (var skvp in wkvp.Value.Scripts) {
                    if (warmed >= limit) {
                        break;
                    }
                    var def = skvp.Value;
                    var lang = def.ScriptTags?.FirstOrDefault() ?? JavaScript;
                    if (!ScriptEngineKinds.JavaScriptAliases.Contains(lang, StringComparer.OrdinalIgnoreCase)) {
                        continue;
                    }
                    try {
                        js.EvaluateScript($"void(a2c.workspaces['{wsName}']['{skvp.Key}'])");
                    }
                    catch { /* ignore */ }
                    warmed++;
                }
            }
        }

    }
    private static string ResolveCSharpTypeToken(string? typeName) {
            // Keep strict: require valid C# keywords or fully-qualified .NET type names.
            // Only handle array suffix; otherwise return the token unchanged.
            if (string.IsNullOrWhiteSpace(typeName)) { return "string"; }
            var t = typeName!.Trim();
            if (t.EndsWith("[]", StringComparison.Ordinal)) {
                var elem = t.Substring(0, t.Length - 2);
                return ResolveCSharpTypeToken(elem) + "[]";
            }
            return t;
    }

    // Minimal, strict conversions for wrapper arguments based on declared type tokens
    private static object?[] ConvertArgsForWrapper(object?[] args, CsWrapperDef def) {
        if (args.Length == 0 || def.Args.Count == 0) { return args; }
        var converted = (object?[])args.Clone();
        int offset = def.HasWorkspace ? 1 : 0; // first arg is the workspace for workspace-scoped wrappers
        for (int i = 0; i < def.Args.Count; i++) {
            int argIndex = offset + i;
            if (argIndex >= converted.Length) { break; }
            var (typeToken, _) = def.Args[i];
            converted[argIndex] = ConvertSingleArg(converted[argIndex], typeToken);
        }
        return converted;
    }

    private static object? ConvertSingleArg(object? value, string typeToken) {
        if (value is null) { return null; }
        var tkn = typeToken?.Trim() ?? string.Empty;
        // Arrays from JSON string, e.g., Int32[]
        if (tkn.EndsWith("[]", StringComparison.Ordinal)) {
            if (value is string s) {
                var elemToken = tkn.Substring(0, tkn.Length - 2);
                if (IsInt32(elemToken)) {
                    return System.Text.Json.JsonSerializer.Deserialize<int[]>(s);
                }
                if (IsString(elemToken)) {
                    return System.Text.Json.JsonSerializer.Deserialize<string[]>(s);
                }
                if (IsDouble(elemToken)) {
                    return System.Text.Json.JsonSerializer.Deserialize<double[]>(s);
                }
                // Unknown element type: return as-is and let Roslyn complain
                return value;
            }
            return value;
        }

        // Dictionary<string, object> from JSON string
        if (IsDictionaryStringObject(tkn)) {
            if (value is string json) {
                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object?>>(json, opts);
            }
            return value;
        }

        // Guid, DateTime, Uri, Enum from string
        if (value is string str) {
            if (IsGuid(tkn)) { return Guid.Parse(str); }
            if (IsDateTime(tkn)) { return DateTime.Parse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind); }
            if (IsUri(tkn)) { return new Uri(str, UriKind.RelativeOrAbsolute); }
            var enumType = TryGetType(tkn);
            if (enumType?.IsEnum == true) {
                return Enum.Parse(enumType, str, ignoreCase: true);
            }
            // Leave other strings as-is
            return value;
        }

        // Numeric to enum support
        var enumT = TryGetType(tkn);
        if (enumT?.IsEnum == true) {
            try {
                if (value is IConvertible) {
                    var underlying = Enum.GetUnderlyingType(enumT);
                    var num = Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
                    return Enum.ToObject(enumT, num!);
                }
            } catch { /* fall through */ }
        }

        // No conversion
        return value;
    }

    private static bool IsInt32(string t) => string.Equals(t, "int", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Int32", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.Int32", StringComparison.OrdinalIgnoreCase);
    private static bool IsDouble(string t) => string.Equals(t, "double", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Double", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.Double", StringComparison.OrdinalIgnoreCase);
    private static bool IsString(string t) => string.Equals(t, "string", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.String", StringComparison.OrdinalIgnoreCase);
    private static bool IsGuid(string t) => string.Equals(t, "Guid", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.Guid", StringComparison.OrdinalIgnoreCase);
    private static bool IsDateTime(string t) => string.Equals(t, "DateTime", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.DateTime", StringComparison.OrdinalIgnoreCase);
    private static bool IsUri(string t) => string.Equals(t, "Uri", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "System.Uri", StringComparison.OrdinalIgnoreCase);
    private static bool IsDictionaryStringObject(string t) {
        // Normalize to remove whitespace variations
        var n = new string((t ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        return n.Equals("System.Collections.Generic.Dictionary<string,object>", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Dictionary<string,object>", StringComparison.OrdinalIgnoreCase);
    }
    private static Type? TryGetType(string typeName) {
        if (string.IsNullOrWhiteSpace(typeName)) { return null; }
        // Try direct lookup first
        var t = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (t != null) { return t; }
        // Fall back to mscorlib/System.Private.CoreLib for BCL types when short names are used
        try { return Type.GetType($"System.{typeName}", throwOnError: false, ignoreCase: true); } catch { return null; }
    }

    // Removed legacy normalization; ResolveCSharpTypeToken handles only [] arrays and otherwise passes through.

    // --- Helpers for orchestrating C# init and script execution ---

    private void ExecuteCSharpInitScripts() {
        var cs = _engineFactory.GetEngine(CSharp);
        var baseConfig = _workspaceService.BaseConfig;

        // Global init for C#
        cs.ExecuteInitScript(baseConfig.InitScript);

        // Workspace init (per workspace, call child then base to match existing JS order)
        foreach (var kvp in baseConfig.Workspaces) {
            var wsName = kvp.Key;
            var ws = kvp.Value;
            ExecuteWorkspaceCSharpInitRecursive(cs, wsName, ws);
        }
    }

    private void ExecuteWorkspaceCSharpInitRecursive(IApi2CliScriptEngine cs, string wsName, WorkspaceDefinition ws) {
        // Execute this workspace's init if C#
        cs.ExecuteInitScript(ws.InitScript);

        // Resolve and execute base chain
        if (!string.IsNullOrEmpty(ws.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(ws.Extend, out var baseWs)) {
            ExecuteWorkspaceCSharpInitRecursive(cs, ws.Extend, baseWs);
        }
    }

    // New: workspace-level C# scriptInit execution (base-first)
    private void ExecuteWorkspaceCSharpScriptInitRecursive(string wsName, WorkspaceDefinition ws, HashSet<string> visiting, HashSet<string> visited) {
        if (visited.Contains(wsName)) { return; }
        if (!visiting.Add(wsName)) {
            // cycle detected; log and bail from this branch
            if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine($"[CS:init] Cycle detected in workspace inheritance at '{wsName}'. Skipping remaining init chain."); } catch { } }
            return;
        }
        // Execute base first if present
        if (!string.IsNullOrEmpty(ws.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(ws.Extend, out var baseWs)) {
            ExecuteWorkspaceCSharpScriptInitRecursive(ws.Extend, baseWs, visiting, visited);
        }
        if (ws.ScriptInit is not null && !string.IsNullOrWhiteSpace(ws.ScriptInit.CSharp)) {
            var cs = _engineFactory.GetEngine(CSharp);
            if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine($"[CS:init] Running init for workspace '{wsName}'"); } catch { } }
            cs.ExecuteInitScript(ws.ScriptInit.CSharp);
        }
        visiting.Remove(wsName);
        visited.Add(wsName);
    }

    private void ExecuteAllWorkspaceCSharpScriptInits() {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] ExecuteAllWorkspaceCSharpScriptInits begin"); } catch { } }
        foreach (var kvp in _workspaceService.BaseConfig.Workspaces) {
            ExecuteWorkspaceCSharpScriptInitRecursive(kvp.Key, kvp.Value, visiting, visited);
        }
    if (IsScriptDebugEnabled()) { try { System.Console.Error.WriteLine("[CS:init] ExecuteAllWorkspaceCSharpScriptInits end"); } catch { } }
    }

    // Remove silent catch helpers per ground rules; let engine throw

    private bool TryGetRequestDefinition(string workspaceName, string requestName, out WorkspaceDefinition? ws, out RequestDefinition? req) {
        ws = null;
        req = null;
        var bc = _workspaceService.BaseConfig;
        if (!bc.Workspaces.TryGetValue(workspaceName, out ws) || ws is null) {
            return false;
        }
        return ws.Requests.TryGetValue(requestName, out req);
    }

    private sealed class PayloadBox {
        public string? Value { get; set; }
    }

    // Strict mode: type resolution and conversions are the responsibility of user scripts.


}

// --- C# handler base-chain generation helpers ---
partial class WorkspaceScriptingOrchestrator {
    // Do not escape quotes for injected C# bodies; strings are inserted via interpolation at runtime
    // so doubling quotes would end up in the Roslyn code and break syntax. Keep as-is.
    private static string Esc(string? s) => s ?? string.Empty;

    // Ensure a C# script body for a Func<...> wrapper always returns a value.
    // Rules:
    // - empty/null -> return null;
    // - contains no ';' and no 'return' -> treat as expression: return (<body>);
    // - contains statements but no 'return' -> append 'return null;' to the end
    // - already contains 'return' -> leave as-is
    private static string EnsureReturns(string? body) {
        var b = body ?? string.Empty;
        var trimmed = b.Trim();

        // Strip block comments /* ... */
        var noBlock = System.Text.RegularExpressions.Regex.Replace(trimmed, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        // Remove whole-line // comments
        var lines = noBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines) {
            var t = line.TrimStart();
            if (t.StartsWith("//")) { continue; }
            sb.AppendLine(line);
        }
        var noComments = sb.ToString().Trim();

        if (string.IsNullOrWhiteSpace(noComments)) {
            return "return null;";
        }

        // naive check is fine here; these are developer-authored short snippets
        var hasReturn = noComments.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0;
        var hasSemicolon = noComments.Contains(';');

        if (!hasReturn && !hasSemicolon) {
            return $"return ({noComments});";
        }

        if (!hasReturn) {
            return noComments + "\nreturn null;";
        }

        return noComments;
    }

    // Roslyn requires non-dynamic arguments when invoking base members; rewrite base.* calls
    // to cast dynamic parameters to object to satisfy the compiler.
    private static string FixBaseCalls(string body) {
        if (string.IsNullOrWhiteSpace(body)) { return body; }
        string b = body;
        b = b.Replace(
            "base.PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs)",
            "base.PreRequest((object)workspace, (object)request, (System.Collections.Generic.IDictionary<string,string>)Headers, (System.Collections.Generic.IList<string>)Parameters, (object)PayloadBox, (System.Collections.Generic.IDictionary<string,string>)Cookies, (object?[])ExtraArgs)");
        b = b.Replace(
            "base.PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs)",
            "base.PostResponse((object)workspace, (object)request, (int)StatusCode, (System.Net.Http.Headers.HttpResponseHeaders)ResponseHeaders, (string)ResponseContent, (object?[])ExtraArgs)");
        return b;
    }
    private static string Sanitize(string name) {
        if (string.IsNullOrEmpty(name)) { return "_"; }
        var sb = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++) {
            char c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_') { sb.Append(c); }
            else { sb.Append('_'); }
        }
        if (char.IsDigit(sb[0])) { sb.Insert(0, '_'); }
        return sb.ToString();
    }

    private void BuildCSharpHandlerChain() {
        var baseConfig = _workspaceService.BaseConfig;
        var cs = _engineFactory.GetEngine(CSharp);
        var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Global handlers
        var globalPreBody = IsCSharp(baseConfig.PreRequest) ? baseConfig.PreRequest!.PayloadAsString ?? string.Empty : string.Empty;
        var globalPostBody = IsCSharp(baseConfig.PostResponse) ? baseConfig.PostResponse!.PayloadAsString ?? string.Empty : "return ResponseContent;";

        // Escape any embedded quotes in bodies for verbatim string
        var gPre = Esc(globalPreBody);
        var gPost = Esc(globalPostBody);
        var globalCode = $@"public class __CsHandlers_Global {{
    public virtual void PreRequest(dynamic workspace, dynamic request, System.Collections.Generic.IDictionary<string,string> Headers, System.Collections.Generic.IList<string> Parameters, dynamic PayloadBox, System.Collections.Generic.IDictionary<string,string> Cookies, params object?[] ExtraArgs) {{
        {globalPreBody}
    }}
    public virtual object? PostResponse(dynamic workspace, dynamic request, int StatusCode, System.Net.Http.Headers.HttpResponseHeaders ResponseHeaders, string ResponseContent, params object?[] ExtraArgs) {{
        {globalPostBody}
    }}
}}
new __CsHandlers_Global()
";
        var gHandler = cs.EvaluateScript(globalCode);
        cs.SetValue("__cs_pre__global", (Action<dynamic, dynamic, IDictionary<string, string>, IList<string>, dynamic, IDictionary<string, string>, object?[]>) ((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
            var h = gHandler; // capture
            if (h == null) { return; }
            ((dynamic) h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
        }));
        cs.SetValue("__cs_post__global", (Func<dynamic, dynamic, int, HttpResponseHeaders, string, object?[], object>) ((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
            var h = gHandler;
            if (h == null) { return ResponseContent; }
            return ((dynamic) h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
        }));

        // Workspace handlers (respect inheritance via Extend)
        foreach (var wkvp in baseConfig.Workspaces) {
            BuildWorkspaceHandlerRecursive(cs, wkvp.Key, wkvp.Value);
        }
    }

    private void BuildWorkspaceHandlerRecursive(IApi2CliScriptEngine cs, string wsName, WorkspaceDefinition ws) {
        string baseClass = "__CsHandlers_Global";
        if (!string.IsNullOrEmpty(ws.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(ws.Extend, out var baseWs)) {
            // Ensure base workspace class exists first
            BuildWorkspaceHandlerRecursive(cs, ws.Extend, baseWs);
            baseClass = $"__CsHandlers_{Sanitize(ws.Extend)}";
        }

        var safeWs = Sanitize(wsName);
        var preBody = IsCSharp(ws.PreRequest)
            ? (ws.PreRequest!.PayloadAsString ?? string.Empty)
            : "base.PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);";
        var postBody = IsCSharp(ws.PostResponse)
            ? (ws.PostResponse!.PayloadAsString ?? string.Empty)
            : "return base.PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs);";

        preBody = FixBaseCalls(preBody);
        postBody = FixBaseCalls(postBody);

        var wsCode = $@"public class __CsHandlers_{safeWs} : {baseClass} {{
    public override void PreRequest(dynamic workspace, dynamic request, System.Collections.Generic.IDictionary<string,string> Headers, System.Collections.Generic.IList<string> Parameters, dynamic PayloadBox, System.Collections.Generic.IDictionary<string,string> Cookies, params object?[] ExtraArgs) {{
    {preBody}
    }}
    public override object? PostResponse(dynamic workspace, dynamic request, int StatusCode, System.Net.Http.Headers.HttpResponseHeaders ResponseHeaders, string ResponseContent, params object?[] ExtraArgs) {{
    {postBody}
    }}
}}
new __CsHandlers_{safeWs}()
";
        var wsHandler = cs.EvaluateScript(wsCode);
        cs.SetValue($"__cs_pre__{safeWs}", (Action<dynamic, dynamic, IDictionary<string, string>, IList<string>, dynamic, IDictionary<string, string>, object?[]>) ((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
            var h = wsHandler;
            if (h == null) { return; }
            ((dynamic) h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
        }));
        cs.SetValue($"__cs_post__{safeWs}", (Func<dynamic, dynamic, int, HttpResponseHeaders, string, object?[], object>) ((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
            var h = wsHandler;
            if (h == null) { return ResponseContent; }
            return ((dynamic) h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
        }));

        // Requests under this workspace
        foreach (var rkvp in ws.Requests) {
            var req = rkvp.Value;
            var reqPre = IsCSharp(req.PreRequest)
                ? (req.PreRequest!.PayloadAsString ?? string.Empty)
                : "base.PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);";
            var reqPost = IsCSharp(req.PostResponse)
                ? (req.PostResponse!.PayloadAsString ?? string.Empty)
                : "return base.PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs);";

            reqPre = FixBaseCalls(reqPre);
            reqPost = FixBaseCalls(reqPost);
            var safeReq = Sanitize(rkvp.Key);
            var reqCode = $@"public class __CsHandlers_{safeWs}__{safeReq} : __CsHandlers_{safeWs} {{
    public override void PreRequest(dynamic workspace, dynamic request, System.Collections.Generic.IDictionary<string,string> Headers, System.Collections.Generic.IList<string> Parameters, dynamic PayloadBox, System.Collections.Generic.IDictionary<string,string> Cookies, params object?[] ExtraArgs) {{
        {reqPre}
    }}
    public override object? PostResponse(dynamic workspace, dynamic request, int StatusCode, System.Net.Http.Headers.HttpResponseHeaders ResponseHeaders, string ResponseContent, params object?[] ExtraArgs) {{
        {reqPost}
    }}
}}
new __CsHandlers_{safeWs}__{safeReq}()
";
            var reqHandler = cs.EvaluateScript(reqCode);
            cs.SetValue($"__cs_pre__{safeWs}__{safeReq}", (Action<dynamic, dynamic, IDictionary<string, string>, IList<string>, dynamic, IDictionary<string, string>, object?[]>) ((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
                var h = reqHandler;
                if (h == null) { return; }
                ((dynamic) h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
            }));
            cs.SetValue($"__cs_post__{safeWs}__{safeReq}", (Func<dynamic, dynamic, int, HttpResponseHeaders, string, object?[], object>) ((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
                var h = reqHandler;
                if (h == null) { return ResponseContent; }
                return ((dynamic) h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
            }));
        }
    }
}
