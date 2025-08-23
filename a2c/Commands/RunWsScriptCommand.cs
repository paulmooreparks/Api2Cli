using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Models;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using Microsoft.ClearScript;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Xfer.Lang;
using System.Globalization;
using ParksComputing.Api2Cli.Diagnostics.Services.Unified; // unified diagnostics

namespace ParksComputing.Api2Cli.Cli.Commands;

internal class RunWsScriptCommand {
    private readonly IWorkspaceService _workspaceService;
    private readonly IApi2CliScriptEngineFactory _scriptEngineFactory;
    private readonly IApi2CliScriptEngine _scriptEngine;
    private readonly IConsoleWriter _console;

    // Cache resolved JS function references to avoid repeated dynamic lookups
    private static readonly ConcurrentDictionary<string, Microsoft.ClearScript.ScriptObject> _jsFuncCache = new();

    // Allow other components to invalidate cache (e.g., when workspaces reload)
    public static void ClearJsFunctionCache() {
        _jsFuncCache.Clear();
    }

    private static bool IsScriptDebugEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

    private static void DebugLog(string message, Exception? ex = null) {
        if (!IsScriptDebugEnabled()) { return; }
        var cw = Utility.GetService<IConsoleWriter>();
        cw?.WriteLine(ex is null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message, category: "cli.debug", code: "debug.message");
    }

    public object? CommandResult { get; private set; } = null;

    public RunWsScriptCommand(
        IWorkspaceService workspaceService,
        IApi2CliScriptEngineFactory scriptEngineFactory,
        IApi2CliScriptEngine scriptEngine,
        IConsoleWriter consoleWriter
        ) {
        _workspaceService = workspaceService;
        _scriptEngineFactory = scriptEngineFactory;
        _scriptEngine = scriptEngine;
        _console = consoleWriter;
    }

    public int Handler(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName
        ) {
        var parseResult = invocationContext.ParseResult;
        return Execute(invocationContext, scriptName, workspaceName, [], parseResult.CommandResult.Tokens);
    }

    public int Execute(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName,
        IEnumerable<object>? args,
        IReadOnlyList<System.CommandLine.Parsing.Token>? tokenArguments
        ) {
        var result = DoCommand(invocationContext, scriptName, workspaceName, args, tokenArguments);

        if (CommandResult is not null && !CommandResult.Equals(Undefined.Value)) {
            _console.WriteLine(CommandResult?.ToString() ?? string.Empty, category: "cli.run", code: "script.result");
        }

        return result;
    }

    public int DoCommand(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName,
        IEnumerable<object>? args,
        IReadOnlyList<System.CommandLine.Parsing.Token>? tokenArguments
        ) {
        if (scriptName is null) {
            _console.WriteError($"{Constants.ErrorChar} Script name is required.", "cli.run", code: "script.missingName");
            return Result.Error;
        }

        // If a workspace was specified, switch and lazily activate it before resolving functions.
        if (!string.IsNullOrEmpty(workspaceName)) {
            _workspaceService.SetActiveWorkspace(workspaceName);
            var orchestrator = Utility.GetService<ParksComputing.Api2Cli.Orchestration.Services.IWorkspaceScriptingOrchestrator>();
            orchestrator?.ActivateWorkspace(workspaceName);
        }

        var paramList = string.Empty;
        var scriptParams = new List<object?>();
        ScriptDefinition? scriptDefinition = null;
        bool found = false;

        if (string.IsNullOrEmpty(workspaceName)) {
            found = _workspaceService.BaseConfig.Scripts.TryGetValue(scriptName, out scriptDefinition);
            if (!found) {
                _console.WriteError($"{Constants.ErrorChar} Script '{scriptName}' not found.", "cli.run", code: "script.notFound", ctx: new Dictionary<string, object?> { ["script"] = scriptName });
                return Result.Error;
            }
        }
        else {
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var workspace)) {
                found = workspace.Scripts.TryGetValue(scriptName, out scriptDefinition);
                if (!found) {
                    _console.WriteError($"{Constants.ErrorChar} Script '{workspaceName}.{scriptName}' not found.", "cli.run", code: "script.notFound", ctx: new Dictionary<string, object?> { ["script"] = scriptName, ["workspace"] = workspaceName });
                    return Result.Error;
                }
            }
            else {
                _console.WriteError($"{Constants.ErrorChar} Workspace '{workspaceName}' not found.", "cli.run", code: "workspace.notFound", ctx: new Dictionary<string, object?> { ["workspace"] = workspaceName });
                return Result.Error;
            }
        }

        if (scriptDefinition is null) {
            _console.WriteError($"{Constants.ErrorChar} Script definition not found", "cli.run", code: "script.definition.missing", ctx: new Dictionary<string, object?> { ["script"] = scriptName, ["workspace"] = workspaceName });
            return Result.Error;
        }

        var argumentDefinitions = scriptDefinition.Arguments.Values.ToList();

        // Determine the resolved language from tags (default to javascript)
        string resolvedLanguage = ScriptEngineKinds.JavaScript;
        resolvedLanguage = scriptDefinition.ScriptTags?.FirstOrDefault() ?? ScriptEngineKinds.JavaScript;

        // Resolve workspace object (used for fallback invocation),
        // but do NOT inject it into scriptParams for a2c.* wrappers.
        object? workspaceObjForFallback = null;
        if (!string.IsNullOrEmpty(workspaceName)) {
            var workspaces = _scriptEngine.Script.a2c.Workspaces as IDictionary<string, object?>;
            if (workspaces is not null && workspaces.TryGetValue(workspaceName, out var ws)) {
                workspaceObjForFallback = ws;
            }
        }

        int i = 0;

        static object? CoerceArg(object? value, string? typeToken) {
            if (value is null) {
                return null;
            }
            // Preserve already-typed values (including objects parsed upstream)
            if (value is not string s) {
                return value;
            }

            // If no type provided, pass through
            var tkn = (typeToken ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(tkn)) {
                return s;
            }

            // Arrays: token like "<T>[]"
            if (tkn.EndsWith("[]", StringComparison.Ordinal)) {
                var elemToken = tkn.Substring(0, tkn.Length - 2);
                return CoerceArrayArg(s, elemToken);
            }

            // Dictionary<string, object>
            if (IsDictionaryStringObject(tkn)) {
                return CoerceDictionaryArg(s);
            }

            // Scalar types and enums
            return CoerceScalarArg(s, tkn);
        }

        static object? CoerceDefault(object? defVal, string? typeToken) {
            if (defVal is null) {
                return null;
            }
            var tkn = (typeToken ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(tkn)) {
                return defVal;
            }
            // If already not a string, keep as-is
            if (defVal is not string s) {
                return defVal;
            }
            if (tkn.EndsWith("[]", StringComparison.Ordinal)) {
                var elem = tkn.Substring(0, tkn.Length - 2);
                return CoerceArrayArg(s, elem);
            }
            if (IsDictionaryStringObject(tkn)) {
                return CoerceDictionaryArg(s);
            }
            return CoerceScalarArg(s, tkn);
        }

        if (tokenArguments is not null && tokenArguments.Any()) {
            using var enumerator = tokenArguments.GetEnumerator();

            foreach (var token in tokenArguments) {
                var arg = token.Value;

                if (i >= argumentDefinitions.Count()) {
                    scriptParams.Add(arg);
                }
                else {
                    var argType = argumentDefinitions[i].Type;
                    scriptParams.Add(CoerceArg(arg, argType));
                }

                i++;
            }
        }
        else if (args is not null) {
            foreach (var arg in args) {
                if (i >= argumentDefinitions.Count()) {
                    scriptParams.Add(arg);
                }
                else {
                    var argType = argumentDefinitions[i].Type;
                    scriptParams.Add(CoerceArg(arg, argType));
                }

                i++;
            }
        }

        if (Console.IsInputRedirected && i < argumentDefinitions.Count) {
            var argString = Console.In.ReadToEnd().Trim();
            var argType = argumentDefinitions[i].Type;
            scriptParams.Add(CoerceArg(argString, argType));

            ++i;
        }

        // Apply default values for any remaining arguments not provided
        while (i < argumentDefinitions.Count) {
            var argDef = argumentDefinitions[i];
            if (argDef.Default is not null) {
                var argType = argDef.Type;
                var defVal = argDef.Default;
                scriptParams.Add(CoerceDefault(defVal, argType));
            }
            else {
                // No default; pad with null to keep indexes consistent for wrappers
                scriptParams.Add(null);
            }
            i++;
        }

        // First try to resolve and invoke via a2c.* function references (JS/ClearScript path)
        // Root script: a2c.{scriptName}(...args)
        // Workspace script: a2c.workspaces.{workspace}.{scriptName}(...args)
        bool invokedViaReference = false;
        try {
            var jsEngineForRef = _scriptEngineFactory.GetEngine(ScriptEngineKinds.JavaScript);
            dynamic scriptRoot = jsEngineForRef.Script;
            dynamic a2c = scriptRoot.a2c;
            static string JsEscape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");


            if (string.IsNullOrEmpty(workspaceName)) {
                var cacheKey = $"js:root:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {

                    CommandResult = cachedFunc.Invoke(false, scriptParams.ToArray());
                    invokedViaReference = true;
                }
                else {
                    // Lookup function at a2c['{scriptName}'] via JS evaluation to respect DynamicObject semantics
                    object? func = jsEngineForRef.EvaluateScript($"a2c['{JsEscape(scriptName)}']");
                    if (func is Microsoft.ClearScript.ScriptObject so) {
                        CommandResult = so.Invoke(false, scriptParams.ToArray());
                        _jsFuncCache.TryAdd(cacheKey, so);

                        invokedViaReference = true;
                    }
                }
            }
            else {
                var cacheKey = $"js:ws:{workspaceName}:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {

                    CommandResult = cachedFunc.Invoke(false, scriptParams.ToArray());
                    invokedViaReference = true;
                }
                else {
                    // Lookup function via JS evaluation to handle ExpandoObject + DynamicObject cases
                    object? func = jsEngineForRef.EvaluateScript($"(a2c.workspaces && a2c.workspaces['{JsEscape(workspaceName)}']) ? a2c.workspaces['{JsEscape(workspaceName)}']['{JsEscape(scriptName)}'] : undefined");
                    if (func is Microsoft.ClearScript.ScriptObject so) {
                        try {
                            // Workspace wrapper already injects the workspace, so pass user args only
                            CommandResult = so.Invoke(false, scriptParams.ToArray());
                            _jsFuncCache.TryAdd(cacheKey, so);
                            invokedViaReference = true;
                        }
                        catch (Microsoft.ClearScript.ScriptEngineException ex) {
                            var details = ex.ErrorDetails;
                            Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "script.invoke.reference.error", ex: ex, ctx: new Dictionary<string, object?> { ["workspace"] = workspaceName, ["script"] = scriptName, ["details"] = details });
                            if (IsScriptDebugEnabled()) {
                                _console.WriteLine($"[JS:err-probe] a2c.workspaces['{workspaceName}']['{scriptName}'] threw: {details}", category: "cli.debug", code: "script.invoke.reference.error");
                            }
                        }
                        // fall back to __script__ path below
                    }
                }
            }
        }
        catch (Microsoft.ClearScript.ScriptEngineException ex) {
            Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "script.invoke.reference.directError", ex: ex, ctx: new Dictionary<string, object?> { ["script"] = scriptName, ["workspace"] = workspaceName, ["details"] = ex.ErrorDetails });
            if (IsScriptDebugEnabled()) { _console.WriteLine($"[JS:err-probe] Direct a2c reference invocation threw: {ex.ErrorDetails}", category: "cli.debug", code: "script.invoke.reference.directError"); }
        }

        if (!invokedViaReference) {
            // Fallback to the unified invoker: a2c.__callScript(wsName|null, scriptName, args)
            try {
                var jsEngine = _scriptEngineFactory.GetEngine(ScriptEngineKinds.JavaScript);
                dynamic scriptRoot2 = jsEngine.Script;
                dynamic a2c2 = scriptRoot2.a2c;
                if (string.IsNullOrEmpty(workspaceName)) {
                    // Root script: no workspace
                    CommandResult = a2c2.__callScript(null, scriptName, scriptParams.ToArray());
                    // Promote cache for next time
                    PromoteReferenceToCache(scriptName, null);
                }
                else {
                    // Workspace script
                    CommandResult = a2c2.__callScript(workspaceName, scriptName, scriptParams.ToArray());
                    PromoteReferenceToCache(scriptName, workspaceName);
                }
            }
            catch (Microsoft.ClearScript.ScriptEngineException ex) {
                // Show concise first line + a trimmed stack excerpt immediately (no env var needed)
                var detailsRaw = ex.ErrorDetails ?? ex.Message;
                var lines = detailsRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var firstLine = lines.FirstOrDefault() ?? ex.Message;
                // Strip duplicated leading "Error:" prefix if present (ClearScript includes this already)
                var cleanedFirst = firstLine.Trim();
                if (cleanedFirst.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) {
                    cleanedFirst = cleanedFirst.Substring(6).TrimStart();
                }
                // Find first script frame line (e.g. "at Script [45] [temp]:6:29 ->  code")
                string? frameLine = lines.Skip(1).FirstOrDefault(l => l.TrimStart().StartsWith("at Script ["));
                int? frameLineNumber = null;
                int? frameColumnNumber = null;
                string? frameSnippet = null;
                if (!string.IsNullOrEmpty(frameLine)) {
                    // Parse pattern
                    var m = System.Text.RegularExpressions.Regex.Match(frameLine, @"at Script \[\d+\] \[temp\]:(\d+):(\d+) *-> *(.*)");
                    if (m.Success) {
                        if (int.TryParse(m.Groups[1].Value, out var ln)) {
                            frameLineNumber = ln;
                        }
                        if (int.TryParse(m.Groups[2].Value, out var col)) {
                            frameColumnNumber = col;
                        }
                        frameSnippet = m.Groups[3].Value.Trim();
                    }
                }
                var fullName = string.IsNullOrEmpty(workspaceName) ? scriptName : workspaceName + "." + scriptName;
                var locationPart = frameLineNumber is null ? string.Empty : (frameSnippet is not null && frameSnippet.Length > 0 ? $" (line {frameLineNumber}: {frameSnippet})" : $" (line {frameLineNumber})");
                // Compose concise display
                var concise = $"{cleanedFirst}{locationPart}";
                Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "script.invoke.callScript.error", ex: ex, ctx: new Dictionary<string, object?> {
                    ["script"] = scriptName,
                    ["workspace"] = workspaceName,
                    ["details"] = detailsRaw,
                    ["firstLine"] = firstLine,
                    ["parsedLine"] = frameLineNumber,
                    ["parsedColumn"] = frameColumnNumber,
                    ["snippet"] = frameSnippet,
                    ["rawFrameLine"] = frameLine
                });
                _console.WriteError($"{Constants.ErrorChar} Script '{fullName}' failed: {concise}", "cli.run", code: "script.invoke.callScript.error", ex: ex, ctx: new Dictionary<string, object?> {
                    ["script"] = scriptName,
                    ["workspace"] = workspaceName,
                    ["message"] = cleanedFirst,
                    ["line"] = frameLineNumber,
                    ["column"] = frameColumnNumber,
                    ["snippet"] = frameSnippet
                });
                return Result.Error;
            }
            catch (Exception ex) {
                var root = ex.GetBaseException();
                var msgLine = (root.Message ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? root.Message;
                var trace = root.StackTrace?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "script.invoke.unhandled", ex: root, ctx: new Dictionary<string, object?> { ["script"] = scriptName, ["workspace"] = workspaceName, ["message"] = root.Message, ["traceFirst"] = trace });
                var fullName2 = string.IsNullOrEmpty(workspaceName) ? scriptName : workspaceName + "." + scriptName;
                _console.WriteError($"{Constants.ErrorChar} Script '{fullName2}' failed: {msgLine}{(string.IsNullOrWhiteSpace(trace) ? string.Empty : " :: " + trace)}", "cli.run", code: "script.invoke.unhandled", ex: root, ctx: new Dictionary<string, object?> { ["script"] = scriptName, ["workspace"] = workspaceName, ["message"] = root.Message });
                return Result.Error;
            }
        }
        return Result.Success;
    }

    // === Typed CLI conversion helpers ===
    private static bool IsDictionaryStringObject(string t) {
        var n = new string((t ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        return n.Equals("System.Collections.Generic.Dictionary<string,object>", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Dictionary<string,object>", StringComparison.OrdinalIgnoreCase);
    }

    private static object? CoerceScalarArg(string s, string typeToken) {
        try {
            switch (typeToken.Trim()) {
                case "string" or "System.String":
                    return s;
                case "bool" or "Boolean" or "System.Boolean":
                    return Convert.ToBoolean(s, CultureInfo.InvariantCulture);
                case "int" or "Int32" or "System.Int32":
                    return Convert.ToInt32(s, CultureInfo.InvariantCulture);
                case "long" or "Int64" or "System.Int64":
                    return Convert.ToInt64(s, CultureInfo.InvariantCulture);
                case "double" or "Double" or "System.Double" or "number":
                    return Convert.ToDouble(s, CultureInfo.InvariantCulture);
                case "float" or "Single" or "System.Single":
                    return Convert.ToSingle(s, CultureInfo.InvariantCulture);
                case "decimal" or "System.Decimal":
                    return Convert.ToDecimal(s, CultureInfo.InvariantCulture);
                case "Guid" or "System.Guid":
                    return Guid.Parse(s);
                case "DateTime" or "System.DateTime":
                    return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                case "Uri" or "System.Uri":
                    return new Uri(s, UriKind.RelativeOrAbsolute);
                default:
                    {
                        var t = TryGetType(typeToken);
                        if (t?.IsEnum == true) {
                            // Allow numeric or name
                            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)) {
                                var underlying = Enum.GetUnderlyingType(t);
                                var val = Convert.ChangeType(num, underlying, CultureInfo.InvariantCulture);
                                return Enum.ToObject(t, val!);
                            }
                            return Enum.Parse(t, s, ignoreCase: true);
                        }
                        // Unknown custom: pass string
                        return s;
                    }
            }
        }
        catch (Exception ex) {
            Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "arg.convert.failure", ex: ex, ctx: new Dictionary<string, object?> { ["value"] = s, ["targetType"] = typeToken });
            Utility.GetService<IConsoleWriter>()?.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Argument conversion failed for value '{s}' -> {typeToken}: {ex.Message}", "cli.run", code: "arg.convert.failure", ex: ex, ctx: new Dictionary<string, object?> { ["value"] = s, ["targetType"] = typeToken });
            throw;
        }
    }

    private static object? CoerceArrayArg(string s, string elemToken) {
        // @file.xfer or inline Xfer [ ... ]
        try {
            string content = s;
            if (s.StartsWith("@", StringComparison.Ordinal)) {
                var path = s.Substring(1);
                content = File.ReadAllText(path);
            }
            if (content.TrimStart().StartsWith("[")) {
                // Try parse via Xfer first; falls back to simple split
                try {
                    var x = XferParser.Parse(content);
                    var arr = ParksComputing.Xfer.Lang.XferConvert.Deserialize<List<object?>>(x);
                    if (arr is null) {
                        return Array.Empty<object?>();
                    }
                    return arr.Select(v => v is string sv ? CoerceScalarArg(sv, elemToken) : v).ToArray();
                }
                catch (Exception ex) {
                    // Fall back to CSV parsing; log only when debug enabled
                    DebugLog($"[Run] Array Xfer parse failed, falling back to CSV (first 200 chars): '{(content.Length > 200 ? content[..200] + "…" : content)}'", ex);
                }
            }
            // Comma-separated fallback
            var parts = content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Select(p => CoerceScalarArg(p, elemToken)).ToArray();
        }
        catch (Exception ex) {
            Utility.GetService<IUnifiedDiagnostics>()?.Error("cli.run", "arg.array.convert.failure", ex: ex, ctx: new Dictionary<string, object?> { ["value"] = s, ["elemType"] = elemToken });
            Utility.GetService<IConsoleWriter>()?.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Array argument conversion failed: {ex.Message}", "cli.run", code: "arg.array.convert.failure", ex: ex, ctx: new Dictionary<string, object?> { ["value"] = s, ["elemType"] = elemToken });
            throw;
        }
    }

    private static object? CoerceDictionaryArg(string s) {
        try {
            string content = s;
            if (s.StartsWith("@", StringComparison.Ordinal)) {
                var path = s.Substring(1);
                content = File.ReadAllText(path);
            }
            if (content.TrimStart().StartsWith("{")) {
                var x = XferParser.Parse(content);
                var dict = ParksComputing.Xfer.Lang.XferConvert.Deserialize<Dictionary<string, object?>>(x);
                return dict;
            }
            // If not Xfer object, pass through as string; orchestration/JS can still handle native objects
            return s;
        }
        catch (Exception ex) {
            // Be forgiving: if it's not valid Xfer, just return the original string and let downstream handle it
            DebugLog($"[Run] Dictionary arg parse failed, passing raw string: '{(s.Length > 200 ? s[..200] + "…" : s)}'", ex);
            return s;
        }
    }

    private static Type? TryGetType(string typeName) {
        if (string.IsNullOrWhiteSpace(typeName)) {
            return null;
        }
        var t = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (t != null) {
            return t;
        }
        try { return Type.GetType($"System.{typeName}", throwOnError: false, ignoreCase: true); }
    catch (Exception ex) { /* debug logging only if enabled */ DebugLog($"[Run] TryGetType fallback failed for '{typeName}'", ex); return null; }
    }

    // After a successful fallback invocation, try to resolve the canonical a2c.* reference and cache it.
    private void PromoteReferenceToCache(string scriptName, string? workspaceName) {
        dynamic scriptRoot = _scriptEngine.Script;
        dynamic a2c = scriptRoot.a2c;
        static string JsEscape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

        if (string.IsNullOrEmpty(workspaceName)) {
            var cacheKey = $"js:root:{scriptName}";

            if (_jsFuncCache.ContainsKey(cacheKey)) {
                return;
            }

            object? func = _scriptEngine.EvaluateScript($"a2c['{JsEscape(scriptName)}']");

            if (func is Microsoft.ClearScript.ScriptObject so) {
                _jsFuncCache.TryAdd(cacheKey, so);
            }
        }
        else {
            var cacheKey = $"js:ws:{workspaceName}:{scriptName}";

            if (_jsFuncCache.ContainsKey(cacheKey)) {
                return;
            }

            object? func = _scriptEngine.EvaluateScript($"(a2c.workspaces && a2c.workspaces['{JsEscape(workspaceName)}']) ? a2c.workspaces['{JsEscape(workspaceName)}']['{JsEscape(scriptName)}'] : undefined");

            if (func is Microsoft.ClearScript.ScriptObject so) {
                _jsFuncCache.TryAdd(cacheKey, so);
            }
        }
    }

    // Probe whether a script is registered and where. Returns true if resolvable at runtime.
    // location: one of "root:a2c", "workspace:a2c", "root:__script__", "workspace:__script__", or "none".
    public bool TryResolveScriptFunction(string scriptName, string? workspaceName, out string location, out string language) {
        location = "none";
        language = ScriptEngineKinds.JavaScript;

        if (string.IsNullOrEmpty(scriptName)) {
            return false;
        }

        // Get definition and language
        ScriptDefinition? scriptDefinition = null;
        if (string.IsNullOrEmpty(workspaceName)) {
            _workspaceService.BaseConfig.Scripts.TryGetValue(scriptName, out scriptDefinition);
        }
        else if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var ws)) {
            ws.Scripts.TryGetValue(scriptName, out scriptDefinition);
        }

        if (scriptDefinition is not null) {
            language = scriptDefinition.ScriptTags?.FirstOrDefault() ?? ScriptEngineKinds.JavaScript;
        }

        // JS path: attempt direct a2c.* resolution (evaluate in engine to respect dynamic/indexer semantics)
        {
            static string JsEscape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

            // Only probe when a2c is present
            var a2cAvailableObj = _scriptEngine.EvaluateScript("typeof a2c !== 'undefined' && a2c != null");
            var a2cAvailable = a2cAvailableObj is bool b && b;
            if (a2cAvailable) {
                if (string.IsNullOrEmpty(workspaceName)) {
                    var cacheKey = $"js:root:{scriptName}";

                    if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                        location = "root:a2c";
                        return true;
                    }

                    object? func = _scriptEngine.EvaluateScript($"a2c['{JsEscape(scriptName)}']");

                    if (func is Microsoft.ClearScript.ScriptObject so) {
                        _jsFuncCache.TryAdd(cacheKey, so);
                        location = "root:a2c";
                        return true;
                    }
                }
                else {
                    var cacheKey = $"js:ws:{workspaceName}:{scriptName}";

                    if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                        location = "workspace:a2c";
                        return true;
                    }

                    object? func = _scriptEngine.EvaluateScript($"(a2c.workspaces && a2c.workspaces['{JsEscape(workspaceName)}']) ? a2c.workspaces['{JsEscape(workspaceName)}']['{JsEscape(scriptName)}'] : undefined");

                    if (func is Microsoft.ClearScript.ScriptObject so) {
                        _jsFuncCache.TryAdd(cacheKey, so);
                        location = "workspace:a2c";
                        return true;
                    }
                }
            }
        }

        // Fallback: check for __script__ function existence using a light eval (JS only)
        if (string.IsNullOrEmpty(workspaceName)) {
            var test = _scriptEngine.EvaluateScript($"typeof __script__{scriptName} === 'function'");
            var isFunc = test is bool tb && tb;

            if (isFunc) {
                location = "root:__script__";
                return true;
            }
        }
        else {
            var test = _scriptEngine.EvaluateScript($"typeof __script__{workspaceName}__{scriptName} === 'function'");
            var isFunc = test is bool tb && tb;

            if (isFunc) {
                location = "workspace:__script__";
                return true;
            }
        }

        return false;
    }
}
