using System;
using System.Collections.Generic;
using System.Linq;

using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Xfer.Lang;
using ParksComputing.Api2Cli.Workspace.Models;
using System.Net.Http.Headers;
// Note: Do not depend on CLI layer here.

namespace ParksComputing.Api2Cli.Orchestration.Services.Impl;

internal class WorkspaceScriptingOrchestrator : IWorkspaceScriptingOrchestrator {
    private readonly IApi2CliScriptEngineFactory _engineFactory;
    private readonly IWorkspaceService _workspaceService;

    public WorkspaceScriptingOrchestrator(IApi2CliScriptEngineFactory engineFactory, IWorkspaceService workspaceService) {
        _engineFactory = engineFactory;
        _workspaceService = workspaceService;
    }

    public void Initialize() {
        var js = _engineFactory.GetEngine("javascript");
        js.InitializeScriptEnvironment();

        var cs = _engineFactory.GetEngine("csharp");
        cs.InitializeScriptEnvironment();

        // CLI-level JS function cache is process-local; nothing to clear here.

        // Bridge for invoking C# script wrappers from JS: expose a2cCsharpInvoke and a2c.csharpInvoke(name, argsArray)
        js.AddHostObject("a2cCsharpInvoke", new Func<string, object?, object?>((fname, argsObj) => {
            object?[] argsArray;
            switch (argsObj) {
                case null:
                    argsArray = Array.Empty<object?>();
                    break;
                case object?[] arr:
                    argsArray = arr;
                    break;
                case System.Collections.IEnumerable enumerable when argsObj is not string:
                    var list = new List<object?>();
                    foreach (var item in enumerable) {
                        list.Add(item);
                    }
                    argsArray = list.ToArray();
                    break;
                default:
                    argsArray = new object?[] { argsObj };
                    break;
            }
            return cs.Invoke(fname, argsArray);
        }));
        js.EvaluateScript("a2c.csharpInvoke = function(name, args) { return a2cCsharpInvoke(name, args); };");

        // Execute new global scriptInit if present: C# first, then JavaScript
        var baseConfig = _workspaceService.BaseConfig;
        if (baseConfig.ScriptInit is not null) {
            if (!string.IsNullOrWhiteSpace(baseConfig.ScriptInit.CSharp)) {
                cs.ExecuteInitScript(baseConfig.ScriptInit.CSharp);
            }
            if (!string.IsNullOrWhiteSpace(baseConfig.ScriptInit.JavaScript)) {
                js.ExecuteInitScript(baseConfig.ScriptInit.JavaScript);
            }
        }

        // Execute per-workspace C# scriptInit when provided (base-first), else run legacy C# init chain
        if (baseConfig.ScriptInit is null) {
            ExecuteCSharpInitScripts();
        } else {
            foreach (var kvp in baseConfig.Workspaces) {
                ExecuteWorkspaceCSharpScriptInitRecursive(kvp.Key, kvp.Value);
            }
        }

    // Note: Per-workspace JavaScript init is executed inside the JS engine initialization
    // (ClearScriptEngine.InitializeScriptEnvironment) to ensure base-first ordering and workspace binding.

        // Register root scripts into JS engine's a2c and C# engine globals
        foreach (var script in _workspaceService.BaseConfig.Scripts) {
            var name = script.Key;
            var def = script.Value;
            var (lang, body) = def.ResolveLanguageAndBody();
            var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));

            if (string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) {
                var source = $@"function __script__{name}({paramList}) {{
{body}
}};

a2c.{name} = __script__{name};";
                js.EvaluateScript(source);
            }
            else if (string.Equals(lang, "csharp", StringComparison.OrdinalIgnoreCase)) {
                // Build a C# delegate wrapper: store under __script__{name} in C# engine, and expose a2c.{name} in JS to call back into C#
                // C#: compile a delegate that accepts object?[] and runs the script body; result assigned to globals["__script__{name}"]
                var argDecl = def.Arguments.Any()
                    ? string.Join("\n    ", def.Arguments.Select((a, idx) => BuildCsArgDeclaration(a.Value.Type, a.Value.Name ?? a.Key, idx)))
                    : string.Empty;
                // If the body looks like a simple expression (no semicolon and no 'return'), wrap it in a return statement
                var scriptBody = body;
                if (!(body.Contains(";") || body.Contains("return", StringComparison.OrdinalIgnoreCase))) {
                    scriptBody = $"return ({body});";
                }
                var csWrapper = $@"
// Root C# script wrapper for {name}
var __script__{name} = new System.Func<object?[], object?>(args => {{
    {argDecl}
    {scriptBody}
    return null;
}});
__script__{name}
";
                var compiled = cs.EvaluateScript(csWrapper);
                cs.SetValue($"__script__{name}", compiled);

                // JS shim so a2c.{name}(...) routes to C# global __script__{name} via array argument
                var jsShim = $@"a2c['{name}'] = (...args) => a2c.csharpInvoke('__script__{name}', args);";
                js.EvaluateScript(jsShim);
            }
        }

        // Register workspace scripts wrappers for both JS and C#; assume a2c.workspaces exists
        foreach (var wkvp in _workspaceService.BaseConfig.Workspaces) {
            var wsName = wkvp.Key;
            var ws = wkvp.Value;
            foreach (var script in ws.Scripts) {
                var name = script.Key;
                var def = script.Value;
                var (lang, body) = def.ResolveLanguageAndBody();
                var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));
                if (string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) {
                    var source =
                    $@"function __script__{wsName}__{name}(workspace, {paramList}) {{
{body}
}};

a2c.workspaces['{wsName}']['{name}'] = (" + (string.IsNullOrEmpty(paramList) ? "" : paramList + ", ") + $"...rest) => __script__{wsName}__{name}(a2c.workspaces['{wsName}'], " + (string.IsNullOrEmpty(paramList) ? "" : paramList + ", ") + "...rest);";
                    js.EvaluateScript(source);
                }
                else if (string.Equals(lang, "csharp", StringComparison.OrdinalIgnoreCase)) {
                    // C#: wrapper takes object?[]; arg0 is workspace, next are declared params
                    var argDecl = def.Arguments.Any()
                        ? string.Join("\n    ", def.Arguments.Select((a, idx) => BuildCsArgDeclaration(a.Value.Type, a.Value.Name ?? a.Key, idx + 1)))
                        : string.Empty;
                    var scriptBody = body;
                    if (!(body.Contains(";") || body.Contains("return", StringComparison.OrdinalIgnoreCase))) {
                        scriptBody = $"return ({body});";
                    }
                    var csWrapper = $@"
// Workspace C# script wrapper for {wsName}.{name}
var __script__{wsName}__{name} = new System.Func<object?[], object?>(args => {{
    dynamic workspace = args.Length > 0 ? args[0] : null;
    {argDecl}
    {scriptBody}
    return null;
}});
__script__{wsName}__{name}
";
                    var compiled = cs.EvaluateScript(csWrapper);
                    cs.SetValue($"__script__{wsName}__{name}", compiled);

                    // JS shim wrapper for workspace C# script to inject workspace first into argument array
                    var jsShim = $@"a2c.workspaces['{wsName}']['{name}'] = (...args) => a2c.csharpInvoke('__script__{wsName}__{name}', [a2c.workspaces['{wsName}'], ...args]);";
                    js.EvaluateScript(jsShim);
                }
            }
        }

        // C# init already executed above to satisfy wrapper dependencies
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
    var js = _engineFactory.GetEngine("javascript");
    var cs = _engineFactory.GetEngine("csharp");

        // Prepare C# globals for mutation from scripts
        var payloadBox = new PayloadBox { Value = payload };
        cs.SetValue("Headers", headers);
        cs.SetValue("Parameters", parameters);
        cs.SetValue("Cookies", cookies);
        // Provide hidden globals matching JS side: workspace and request dynamics
        dynamic wsDyn = new System.Dynamic.ExpandoObject();
        wsDyn.name = workspaceName;
        dynamic reqDyn = new System.Dynamic.ExpandoObject();
        reqDyn.name = requestName;
        reqDyn.headers = headers;
        reqDyn.parameters = parameters;
        reqDyn.payload = payload;
        cs.SetValue("workspace", wsDyn);
        cs.SetValue("request", reqDyn);
        cs.SetValue("WorkspaceName", workspaceName);
        cs.SetValue("RequestName", requestName);
        cs.SetValue("ExtraArgs", extraArgs);
        cs.SetValue("PayloadBox", payloadBox);

        // Execute C# preRequest handlers in request -> workspace -> global order (mirrors JS chain semantics)
        if (TryGetRequestDefinition(workspaceName, requestName, out var ws, out var req) && IsCSharp(req?.PreRequest)) {
            cs.ExecuteScript(req!.PreRequest!.PayloadAsString);
        }
        if (ws is not null && IsCSharp(ws.PreRequest)) {
            cs.ExecuteScript(ws.PreRequest!.PayloadAsString);
        }
        if (IsCSharp(baseConfig.PreRequest)) {
            cs.ExecuteScript(baseConfig.PreRequest!.PayloadAsString);
        }

        // Apply any payload change made by C# scripts
        payload = payloadBox.Value;

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
        var js = _engineFactory.GetEngine("javascript");
        var cs = _engineFactory.GetEngine("csharp");

        object? lastCsResult = null;

        // Provide response-related globals to C# scripts
        cs.SetValue("WorkspaceName", workspaceName);
        cs.SetValue("RequestName", requestName);
        cs.SetValue("StatusCode", statusCode);
        cs.SetValue("ResponseHeaders", headers);
        cs.SetValue("ResponseContent", responseContent);
        cs.SetValue("ExtraArgs", extraArgs);

        // Hidden workspace/request dynamics for postResponse
        dynamic wsDyn = new System.Dynamic.ExpandoObject();
        wsDyn.name = workspaceName;
        dynamic reqDyn = new System.Dynamic.ExpandoObject();
        reqDyn.name = requestName;
        reqDyn.response = new System.Dynamic.ExpandoObject();
        reqDyn.response.statusCode = statusCode;
        reqDyn.response.headers = headers;
        reqDyn.response.body = responseContent;
        cs.SetValue("workspace", wsDyn);
        cs.SetValue("request", reqDyn);

        // Execute C# postResponse in request -> workspace -> global order
        if (TryGetRequestDefinition(workspaceName, requestName, out var ws, out var req) && IsCSharp(req?.PostResponse)) {
            lastCsResult = cs.EvaluateScript(req!.PostResponse!.PayloadAsString);
        }
        if (ws is not null && IsCSharp(ws.PostResponse)) {
            lastCsResult = cs.EvaluateScript(ws.PostResponse!.PayloadAsString);
        }
        var baseConfig2 = _workspaceService.BaseConfig;
        if (IsCSharp(baseConfig2.PostResponse)) {
            lastCsResult = cs.EvaluateScript(baseConfig2.PostResponse!.PayloadAsString);
        }

        // Then defer to JS chain; prefer JS result if provided, else return last non-null C# result
        var jsResult = js.InvokePostResponse(workspaceName, requestName, statusCode, headers, responseContent, extraArgs);
        return jsResult ?? lastCsResult;
    }

    private static bool IsCSharp(XferKeyedValue? kv) {
        var lang = kv?.Keys?.FirstOrDefault();
        return string.Equals(lang, "csharp", StringComparison.OrdinalIgnoreCase) || string.Equals(lang, "cs", StringComparison.OrdinalIgnoreCase);
    }

    public void Warmup(int limit = 25, bool enable = false, bool debug = false) {
        if (!enable) {
            return;
        }
        int warmed = 0;
        var js = _engineFactory.GetEngine("javascript");

        // Touch a limited number of JS script references to ensure they are defined
        foreach (var kvp in _workspaceService.BaseConfig.Scripts) {
            if (warmed >= limit) {
                break;
            }
            var def = kvp.Value;
            var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";

            if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) {
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
                    var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
                    if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) {
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
    // Helper to build typed C# argument declarations inside the Roslyn script wrappers
    private static string BuildCsArgDeclaration(string? argType, string varName, int argIndex) {
        var index = argIndex.ToString();
        switch ((argType ?? "string").ToLowerInvariant()) {
            case "number":
                return $"double {varName} = args.Length > {index} ? System.Convert.ToDouble(args[{index}]) : 0d;";
            case "boolean":
                return $"bool {varName} = args.Length > {index} ? System.Convert.ToBoolean(args[{index}]) : false;";
            case "object":
                return $"var {varName} = args.Length > {index} ? args[{index}] : null;";
            case "stringarray":
                // Keep as object; scripts can coerce as needed
                return $"var {varName} = args.Length > {index} ? args[{index}] : null;";
            case "string":
            default:
                return $"string? {varName} = args.Length > {index} ? System.Convert.ToString(args[{index}]) : null;";
        }
    }

    // --- Helpers for orchestrating C# init and script execution ---

    private void ExecuteCSharpInitScripts() {
        var cs = _engineFactory.GetEngine("csharp");
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
    private void ExecuteWorkspaceCSharpScriptInitRecursive(string wsName, WorkspaceDefinition ws) {
        // Execute base first if present
        if (!string.IsNullOrEmpty(ws.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(ws.Extend, out var baseWs)) {
            ExecuteWorkspaceCSharpScriptInitRecursive(ws.Extend, baseWs);
        }
        if (ws.ScriptInit is not null && !string.IsNullOrWhiteSpace(ws.ScriptInit.CSharp)) {
            var cs = _engineFactory.GetEngine("csharp");
            cs.ExecuteInitScript(ws.ScriptInit.CSharp);
        }
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


}
