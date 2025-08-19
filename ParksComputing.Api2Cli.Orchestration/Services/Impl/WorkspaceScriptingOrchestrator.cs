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
    // Parameter type signatures for C# wrappers (used by JS->C# bridge for host-side conversion)
    private readonly Dictionary<string, Type[]> _csParamSigs = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceScriptingOrchestrator(IApi2CliScriptEngineFactory engineFactory, IWorkspaceService workspaceService) {
        _engineFactory = engineFactory;
        _workspaceService = workspaceService;
    }

    public void RegisterRequestExecutor(Func<string, string, object?[]?, object?> executor)
    {
        _requestExecutor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public void Initialize() {
    var js = _engineFactory.GetEngine(JavaScript);
        js.InitializeScriptEnvironment();

    var cs = _engineFactory.GetEngine(CSharp);
        cs.InitializeScriptEnvironment();

        // CLI-level JS function cache is process-local; nothing to clear here.

        // Bridge for invoking C# script wrappers from JS with host-side argument conversion
        js.AddHostObject("a2cCsharpInvoke", new Func<string, object?, object?>((fname, argsObj) => {
            object?[] argsArray = argsObj switch {
                null => Array.Empty<object?>(),
                object?[] arr => arr,
                System.Collections.IEnumerable e when argsObj is not string => e.Cast<object?>().ToArray(),
                _ => new object?[] { argsObj }
            };

            if (_csParamSigs.TryGetValue(fname, out var sig) && sig.Length > 0) {
                argsArray = ConvertArgs(argsArray, sig);
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

            if (string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                // Define as a property assignment with an anonymous function to avoid polluting global scope
                var source = $@"a2c['{name}'] = function({paramList}) {{
{body}
}};";
                js.EvaluateScript(source);
            }
            else if (string.Equals(lang, CSharp, StringComparison.OrdinalIgnoreCase)) {
                // Build a typed C# delegate wrapper: store under __script__{name}
                var paramTypes = def.Arguments.Select(a => ResolveCSharpType(a.Value.Type)).ToArray();
                _csParamSigs[$"__script__{name}"] = paramTypes;

                var csParamSig = def.Arguments.Any()
                    ? string.Join(", ", def.Arguments.Select(a => $"{ResolveCSharpTypeToken(a.Value.Type)} {a.Value.Name ?? a.Key}"))
                    : string.Empty;
                var funcGeneric = def.Arguments.Any()
                    ? string.Join(", ", def.Arguments.Select(a => ResolveCSharpTypeToken(a.Value.Type))) + ", object"
                    : "object";
                var lambdaParams = def.Arguments.Any()
                    ? string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key))
                    : string.Empty;
                var scriptBody = EnsureReturns(body);
                var csWrapper = $@"
// Root C# typed script wrapper for {name}
System.Func<{funcGeneric}> __script__{name} = new System.Func<{funcGeneric}>(({csParamSig}) => {{
    {scriptBody}
}});
__script__{name}
";
                var compiled = cs.EvaluateScript(csWrapper);
                cs.SetValue($"__script__{name}", compiled);

                // JS shim to call C# wrapper (host will convert args)
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
                if (string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                    // Assign a closure that captures workspace as a hidden parameter; avoid global function pollution
                    var source = $@"a2c.workspaces['{wsName}']['{name}'] = (function(workspace) {{ return function({paramList}) {{
{body}
}}; }})(a2c.workspaces['{wsName}']);";
                    js.EvaluateScript(source);
                }
                else if (string.Equals(lang, CSharp, StringComparison.OrdinalIgnoreCase)) {
                    // Build typed C# delegate: first hidden param is workspace (dynamic), followed by declared params
                    var paramTypes = new List<Type> { typeof(object) };
                    paramTypes.AddRange(def.Arguments.Select(a => ResolveCSharpType(a.Value.Type)));
                    _csParamSigs[$"__script__{wsName}__{name}"] = paramTypes.ToArray();

                    var csParamSig = "dynamic workspace" + (def.Arguments.Any()
                        ? ", " + string.Join(", ", def.Arguments.Select(a => $"{ResolveCSharpTypeToken(a.Value.Type)} {a.Value.Name ?? a.Key}"))
                        : string.Empty);
                    var funcGeneric = "object" + (def.Arguments.Any()
                        ? ", " + string.Join(", ", def.Arguments.Select(a => ResolveCSharpTypeToken(a.Value.Type))) + ", object"
                        : ", object");
                    var scriptBody = EnsureReturns(body);
                    var csWrapper = $@"
// Workspace C# typed script wrapper for {wsName}.{name}
System.Func<{funcGeneric}> __script__{wsName}__{name} = new System.Func<{funcGeneric}>(({csParamSig}) => {{
    {scriptBody}
}});
__script__{wsName}__{name}
";
                    var compiled = cs.EvaluateScript(csWrapper);
                    cs.SetValue($"__script__{wsName}__{name}", compiled);

                    // JS shim injects workspace as the first arg; host converts types
                    var jsShim = $@"a2c.workspaces['{wsName}']['{name}'] = (...args) => a2c.csharpInvoke('__script__{wsName}__{name}', [a2c.workspaces['{wsName}'], ...args]);";
                    js.EvaluateScript(jsShim);
                }
            }

            // Expose request executors for this workspace if a client registered one
            if (_requestExecutor is not null)
            {
                foreach (var rkvp in ws.Requests)
                {
                    var reqName = rkvp.Key;
                    // Provide a JS callable function that forwards to the host executor
                    js.AddHostObject($"__invoke_request__{wsName}__{reqName}", new Func<object?[], object?>(args => _requestExecutor(wsName, reqName, args)));
                    js.EvaluateScript($"a2c.workspaces['{wsName}'].requests['{reqName}'].execute = function() {{ return __invoke_request__{wsName}__{reqName}(Array.from(arguments)); }};");
                }
            }
        }

        // C# init already executed above to satisfy wrapper dependencies

    // Build C# handler inheritance chain (global -> workspace -> request) and expose delegates
    BuildCSharpHandlerChain();
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
    var cs = _engineFactory.GetEngine(CSharp);

    // Prepare call inputs for typed C# delegate wrappers
    dynamic payloadBox = new System.Dynamic.ExpandoObject();
    payloadBox.Value = payload;
    var wsDyn = new System.Dynamic.ExpandoObject();
    ((IDictionary<string, object?>)wsDyn)["name"] = workspaceName;
    var reqDyn = new System.Dynamic.ExpandoObject();
    ((IDictionary<string, object?>)reqDyn)["name"] = requestName;
    ((IDictionary<string, object?>)reqDyn)["headers"] = headers;
    ((IDictionary<string, object?>)reqDyn)["parameters"] = parameters;
    ((IDictionary<string, object?>)reqDyn)["payload"] = payload;

    // Invoke only the most-derived C# preRequest delegate; base chaining is handled inside the class
    var reqKey = $"__cs_pre__{Sanitize(workspaceName)}__{Sanitize(requestName)}";
    object?[] callArgs = new object?[] { wsDyn, reqDyn, headers, parameters, payloadBox, cookies, extraArgs };
    cs.Invoke(reqKey, callArgs);

    // Apply any payload change made by C# scripts via payloadBox
    if (payloadBox is IDictionary<string, object?> dict && dict.TryGetValue("Value", out var pv)) {
        payload = pv as string;
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
    var cs = _engineFactory.GetEngine(CSharp);

    object? lastCsResult = null;

    // Prepare inputs and invoke most-derived C# postResponse delegate
    var wsDyn2 = new System.Dynamic.ExpandoObject();
    ((IDictionary<string, object?>)wsDyn2)["name"] = workspaceName;
    var reqDyn2 = new System.Dynamic.ExpandoObject();
    ((IDictionary<string, object?>)reqDyn2)["name"] = requestName;
    var respObj = new System.Dynamic.ExpandoObject();
    ((IDictionary<string, object?>)respObj)["statusCode"] = statusCode;
    ((IDictionary<string, object?>)respObj)["headers"] = headers;
    ((IDictionary<string, object?>)respObj)["body"] = responseContent;
    ((IDictionary<string, object?>)reqDyn2)["response"] = respObj;

    var reqKey2 = $"__cs_post__{Sanitize(workspaceName)}__{Sanitize(requestName)}";
    var wsKey2 = $"__cs_post__{Sanitize(workspaceName)}";
    var globalKey2 = "__cs_post__global";
    object?[] postArgs = new object?[] { wsDyn2, reqDyn2, statusCode, headers, responseContent, extraArgs };
    lastCsResult = cs.Invoke(reqKey2, postArgs);
    if (lastCsResult is null || (lastCsResult is string sres && string.Equals(sres, responseContent, StringComparison.Ordinal)))
    {
        lastCsResult = cs.Invoke(wsKey2, postArgs) ?? cs.Invoke(globalKey2, postArgs);
    }

    // Then defer to JS chain; prefer JS result only when it actually changes the content
    var jsResult = js.InvokePostResponse(workspaceName, requestName, statusCode, headers, responseContent, extraArgs);
    if (jsResult is null)
    {
        return lastCsResult;
    }
    if (jsResult is string jsStr && string.Equals(jsStr, responseContent, StringComparison.Ordinal))
    {
        // JS chain returned original content (no-op); favor C# result if it changed content
        if (lastCsResult is string csStr && !string.Equals(csStr, responseContent, StringComparison.Ordinal))
        {
            return csStr;
        }
    }
    return jsResult ?? lastCsResult;
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
        if (string.IsNullOrWhiteSpace(typeName)) { return "string"; }
        var t = typeName!.Trim();
        var lower = t.ToLowerInvariant();
        // Common mappings and synonyms
        return lower switch {
            "string" or "system.string" => "string",
            "bool" or "boolean" or "system.boolean" => "bool",
            "number" => "double",
            "double" or "system.double" => "double",
            "float" or "single" or "system.single" => "float",
            "decimal" or "system.decimal" => "decimal",
            "int" or "int32" or "system.int32" => "int",
            "long" or "int64" or "system.int64" => "long",
            "short" or "int16" or "system.int16" => "short",
            "byte" or "system.byte" => "byte",
            "sbyte" or "system.sbyte" => "sbyte",
            "uint" or "system.uint32" => "uint",
            "ulong" or "system.uint64" => "ulong",
            "ushort" or "system.uint16" => "ushort",
            "char" or "system.char" => "char",
            "object" or "system.object" => "object",
            "datetime" or "system.datetime" => "System.DateTime",
            "timespan" or "system.timespan" => "System.TimeSpan",
            "guid" or "system.guid" => "System.Guid",
            "uri" or "system.uri" => "System.Uri",
            _ => NormalizePossiblyArrayType(t)
        };
    }

    private static string NormalizePossiblyArrayType(string t) {
        // If user specified FQN or simple name, allow arrays: e.g., "My.Type[]" or "Int32[]"
        if (t.EndsWith("[]", StringComparison.Ordinal)) {
            var elem = t.Substring(0, t.Length - 2);
            var elemToken = ResolveCSharpTypeToken(elem);
            return elemToken + "[]";
        }
        // If looks like simple BCL name (e.g., Int32), qualify to System.Int32 to avoid missing using
        return t.Contains('.') ? t : t switch {
            "String" => "System.String",
            "Boolean" => "System.Boolean",
            "Int32" => "System.Int32",
            "Int64" => "System.Int64",
            "Int16" => "System.Int16",
            "UInt32" => "System.UInt32",
            "UInt64" => "System.UInt64",
            "UInt16" => "System.UInt16",
            "Single" => "System.Single",
            "Double" => "System.Double",
            "Decimal" => "System.Decimal",
            "Guid" => "System.Guid",
            "DateTime" => "System.DateTime",
            "TimeSpan" => "System.TimeSpan",
            "Uri" => "System.Uri",
            _ => t
        };
    }

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
    private void ExecuteWorkspaceCSharpScriptInitRecursive(string wsName, WorkspaceDefinition ws) {
        // Execute base first if present
        if (!string.IsNullOrEmpty(ws.Extend) && _workspaceService.BaseConfig.Workspaces.TryGetValue(ws.Extend, out var baseWs)) {
            ExecuteWorkspaceCSharpScriptInitRecursive(ws.Extend, baseWs);
        }
        if (ws.ScriptInit is not null && !string.IsNullOrWhiteSpace(ws.ScriptInit.CSharp)) {
            var cs = _engineFactory.GetEngine(CSharp);
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

    // --- Host-side argument conversion helpers for JS -> C# bridge ---
    private static object?[] ConvertArgs(object?[] args, Type[] sig) {
        var max = Math.Min(args.Length, sig.Length);
        var result = new object?[max];
        for (int i = 0; i < max; i++) {
            var target = sig[i];
            if (target == null) { result[i] = args[i]; continue; }
            result[i] = ConvertTo(args[i], target);
        }
        return result;
    }

    private static object? ConvertTo(object? value, Type targetType) {
        if (value is null) { return null; }
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(string)) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
        if (underlying.IsEnum) {
            if (value is string es) { return Enum.Parse(underlying, es, true); }
            return Enum.ToObject(underlying, System.Convert.ChangeType(value, Enum.GetUnderlyingType(underlying), CultureInfo.InvariantCulture)!);
        }
        if (underlying == typeof(Guid)) { return value is Guid g ? g : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!); }
        if (underlying == typeof(DateTime)) { return value is DateTime dt ? dt : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        if (underlying == typeof(TimeSpan)) { return value is TimeSpan ts ? ts : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture); }
        if (underlying == typeof(Uri)) { return value is Uri u ? u : new Uri(Convert.ToString(value, CultureInfo.InvariantCulture)!); }

        try { return System.Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture); } catch { }

        if (value is string s) {
            var t = s.TrimStart();
            if (t.StartsWith("{") || t.StartsWith("[")) {
                try { return JsonSerializer.Deserialize(s, targetType)!; } catch { /* fallthrough */ }
            }
            var conv = TypeDescriptor.GetConverter(underlying);
            if (conv != null && conv.CanConvertFrom(typeof(string))) {
                return conv.ConvertFrom(null, CultureInfo.InvariantCulture, s);
            }
        }

        // Last resort: if already assignable
        if (underlying.IsInstanceOfType(value)) { return value; }
        throw new InvalidOperationException($"Cannot convert argument to {underlying.FullName}");
    }

    private static Type ResolveCSharpType(string? typeName) {
        if (string.IsNullOrWhiteSpace(typeName)) { return typeof(string); }
        var t = typeName!.Trim();
        var lower = t.ToLowerInvariant();
        // Common aliases
        switch (lower) {
            case "string" or "system.string": return typeof(string);
            case "bool" or "boolean" or "system.boolean": return typeof(bool);
            case "number" or "double" or "system.double": return typeof(double);
            case "float" or "single" or "system.single": return typeof(float);
            case "decimal" or "system.decimal": return typeof(decimal);
            case "int" or "int32" or "system.int32": return typeof(int);
            case "long" or "int64" or "system.int64": return typeof(long);
            case "short" or "int16" or "system.int16": return typeof(short);
            case "byte" or "system.byte": return typeof(byte);
            case "sbyte" or "system.sbyte": return typeof(sbyte);
            case "uint" or "system.uint32": return typeof(uint);
            case "ulong" or "system.uint64": return typeof(ulong);
            case "ushort" or "system.uint16": return typeof(ushort);
            case "char" or "system.char": return typeof(char);
            case "object" or "system.object": return typeof(object);
            case "datetime" or "system.datetime": return typeof(DateTime);
            case "timespan" or "system.timespan": return typeof(TimeSpan);
            case "guid" or "system.guid": return typeof(Guid);
            case "uri" or "system.uri": return typeof(Uri);
        }
        // Arrays
        if (t.EndsWith("[]", StringComparison.Ordinal)) {
            var elem = t.Substring(0, t.Length - 2);
            var et = ResolveCSharpType(elem);
            return et.MakeArrayType();
        }
        // Simple generic types e.g., Dictionary<string, object>
        var lt = t.IndexOf('<');
        var gt = t.LastIndexOf('>');
        if (lt > 0 && gt > lt)
        {
            var genName = t.Substring(0, lt).Trim();
            var argsList = t.Substring(lt + 1, gt - lt - 1).Trim();
            var argParts = SplitGenericArgs(argsList);
            var argTypes = argParts.Select(ResolveCSharpType).ToArray();
            // Normalize common BCL generic names
            genName = genName switch {
                "Dictionary" => "System.Collections.Generic.Dictionary",
                "List" => "System.Collections.Generic.List",
                "IList" => "System.Collections.Generic.IList",
                "IEnumerable" => "System.Collections.Generic.IEnumerable",
                _ => genName.Contains('.') ? genName : genName
            };
            var genericArity = argTypes.Length;
            var candidate = Type.GetType($"{genName}`{genericArity}", throwOnError: false) ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType($"{genName}`{genericArity}", false)).FirstOrDefault(x => x != null);
            if (candidate != null && candidate.IsGenericTypeDefinition)
            {
                try { return candidate.MakeGenericType(argTypes); } catch { }
            }
        }
        // Try Type.GetType for FQNs
        var fqn = t.Contains('.') ? t : NormalizeBclName(t);
        var ty = Type.GetType(fqn, throwOnError: false, ignoreCase: false);
        if (ty != null) { return ty; }
        // Search loaded assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            ty = asm.GetType(fqn, throwOnError: false, ignoreCase: false);
            if (ty != null) { return ty; }
        }
        // Fallback to object
        return typeof(object);
    }

    private static List<string> SplitGenericArgs(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) { return list; }
        int depth = 0; int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<') { depth++; }
            else if (c == '>') { depth--; }
            else if (c == ',' && depth == 0)
            {
                list.Add(s.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        list.Add(s.Substring(start).Trim());
        return list;
    }

    private static string NormalizeBclName(string simple) => simple switch {
        "String" => "System.String",
        "Boolean" => "System.Boolean",
        "Int32" => "System.Int32",
        "Int64" => "System.Int64",
        "Int16" => "System.Int16",
        "UInt32" => "System.UInt32",
        "UInt64" => "System.UInt64",
        "UInt16" => "System.UInt16",
        "Single" => "System.Single",
        "Double" => "System.Double",
        "Decimal" => "System.Decimal",
        "Guid" => "System.Guid",
        "DateTime" => "System.DateTime",
        "TimeSpan" => "System.TimeSpan",
        "Uri" => "System.Uri",
        _ => simple
    };


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
    private static string EnsureReturns(string? body)
    {
        var b = body ?? string.Empty;
        var trimmed = b.Trim();

        // Strip block comments /* ... */
        var noBlock = System.Text.RegularExpressions.Regex.Replace(trimmed, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        // Remove whole-line // comments
        var lines = noBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("//")) { continue; }
            sb.AppendLine(line);
        }
        var noComments = sb.ToString().Trim();

        if (string.IsNullOrWhiteSpace(noComments))
        {
            return "return null;";
        }

        // naive check is fine here; these are developer-authored short snippets
        var hasReturn = noComments.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0;
        var hasSemicolon = noComments.Contains(';');

        if (!hasReturn && !hasSemicolon)
        {
            return $"return ({noComments});";
        }

        if (!hasReturn)
        {
            return noComments + "\nreturn null;";
        }

        return noComments;
    }

    // Roslyn requires non-dynamic arguments when invoking base members; rewrite base.* calls
    // to cast dynamic parameters to object to satisfy the compiler.
    private static string FixBaseCalls(string body)
    {
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
        cs.SetValue("__cs_pre__global", (Action<dynamic,dynamic,IDictionary<string,string>,IList<string>,dynamic,IDictionary<string,string>,object?[]>)((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
            var h = gHandler; // capture
            if (h == null) { return; }
            ((dynamic)h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
        }));
        cs.SetValue("__cs_post__global", (Func<dynamic,dynamic,int,HttpResponseHeaders,string,object?[],object>)((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
            var h = gHandler;
            if (h == null) { return ResponseContent; }
            return ((dynamic)h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
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
        cs.SetValue($"__cs_pre__{safeWs}", (Action<dynamic,dynamic,IDictionary<string,string>,IList<string>,dynamic,IDictionary<string,string>,object?[]>)((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
            var h = wsHandler;
            if (h == null) { return; }
            ((dynamic)h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
        }));
        cs.SetValue($"__cs_post__{safeWs}", (Func<dynamic,dynamic,int,HttpResponseHeaders,string,object?[],object>)((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
            var h = wsHandler;
            if (h == null) { return ResponseContent; }
            return ((dynamic)h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
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
            cs.SetValue($"__cs_pre__{safeWs}__{safeReq}", (Action<dynamic,dynamic,IDictionary<string,string>,IList<string>,dynamic,IDictionary<string,string>,object?[]>)((workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs) => {
                var h = reqHandler;
                if (h == null) { return; }
                ((dynamic)h).PreRequest(workspace, request, Headers, Parameters, PayloadBox, Cookies, ExtraArgs);
            }));
            cs.SetValue($"__cs_post__{safeWs}__{safeReq}", (Func<dynamic,dynamic,int,HttpResponseHeaders,string,object?[],object>)((workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) => {
                var h = reqHandler;
                if (h == null) { return ResponseContent; }
                return ((dynamic)h).PostResponse(workspace, request, StatusCode, ResponseHeaders, ResponseContent, ExtraArgs) ?? ResponseContent;
            }));
        }
    }
}
