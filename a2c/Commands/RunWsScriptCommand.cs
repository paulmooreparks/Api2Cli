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

namespace ParksComputing.Api2Cli.Cli.Commands;

internal class RunWsScriptCommand {
    private readonly IWorkspaceService _workspaceService;
    private readonly IApi2CliScriptEngineFactory _scriptEngineFactory;
    private readonly IApi2CliScriptEngine _scriptEngine;

    // Cache resolved JS function references to avoid repeated dynamic lookups
    private static readonly ConcurrentDictionary<string, Microsoft.ClearScript.ScriptObject> _jsFuncCache = new();

    // Allow other components to invalidate cache (e.g., when workspaces reload)
    public static void ClearJsFunctionCache()
    {
        _jsFuncCache.Clear();
        if (IsScriptDebugEnabled()) {
            Console.Error.WriteLine("[script-debug] JS function cache cleared");
        }
    }

    private static bool IsScriptDebugEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);

    public object? CommandResult { get; private set; } = null;

    public RunWsScriptCommand(
        IWorkspaceService workspaceService,
        IApi2CliScriptEngineFactory scriptEngineFactory,
        IApi2CliScriptEngine scriptEngine
        )
    {
        _workspaceService = workspaceService;
        _scriptEngineFactory = scriptEngineFactory;
        _scriptEngine = scriptEngine;
    }

    public int Handler(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName
        )
    {
        var parseResult = invocationContext.ParseResult;
        return Execute(invocationContext, scriptName, workspaceName, [], parseResult.CommandResult.Tokens);
    }

    public int Execute(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName,
        IEnumerable<object>? args,
        IReadOnlyList<System.CommandLine.Parsing.Token>? tokenArguments
        )
    {
        var result = DoCommand(invocationContext, scriptName, workspaceName, args, tokenArguments);

        if (CommandResult is not null && !CommandResult.Equals(Undefined.Value)) {
            Console.WriteLine(CommandResult);
        }

        return result;
    }

    public int DoCommand(
        InvocationContext invocationContext,
        string scriptName,
        string? workspaceName,
        IEnumerable<object>? args,
        IReadOnlyList<System.CommandLine.Parsing.Token>? tokenArguments
        )
    {
        if (scriptName is null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Script name is required.");
            return Result.Error;
        }

        var paramList = string.Empty;
        var scriptParams = new List<object?>();
        ScriptDefinition? scriptDefinition = null;
        bool found = false;

        if (string.IsNullOrEmpty(workspaceName)) {
            found = _workspaceService.BaseConfig.Scripts.TryGetValue(scriptName, out scriptDefinition);
            if (!found) {
                Console.Error.WriteLine($"{Constants.ErrorChar} Script '{scriptName}' not found.");
                return Result.Error;
            }
        }
        else {
            if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var workspace)) {
                found = workspace.Scripts.TryGetValue(scriptName, out scriptDefinition);
                if (!found) {
                    Console.Error.WriteLine($"{Constants.ErrorChar} Script '{workspaceName}.{scriptName}' not found.");
                    return Result.Error;
                }
            }
            else {
                Console.Error.WriteLine($"{Constants.ErrorChar} Workspace '{workspaceName}' not found.");
                return Result.Error;
            }
        }

        if (scriptDefinition is null) {
            Console.Error.WriteLine($"{Constants.ErrorChar} Script definition not found");
            return Result.Error;
        }

        var argumentDefinitions = scriptDefinition.Arguments.Values.ToList();

        // Determine the resolved language from tags (default to javascript)
        string resolvedLanguage = "javascript";
        try {
            resolvedLanguage = scriptDefinition.ScriptTags?.FirstOrDefault() ?? "javascript";
        }
        catch { /* ignore */ }

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

        if (tokenArguments is not null && tokenArguments.Any()) {
            using var enumerator = tokenArguments.GetEnumerator();

            foreach (var token in tokenArguments) {
                var arg = token.Value;

                if (i >= argumentDefinitions.Count()) {
                    scriptParams.Add(arg);
                }
                else {
                    var argType = argumentDefinitions[i].Type;

                    switch (argType) {
                        case "string":
                            scriptParams.Add(arg);
                            break;

                        case "stringArray":
                            scriptParams.Add(arg);
                            break;

                        case "number":
                            scriptParams.Add(Convert.ToDouble(arg));
                            break;

                        case "boolean":
                            scriptParams.Add(Convert.ToBoolean(arg));
                            break;

                        case "object":
                            scriptParams.Add(arg);
                            break;

                        default:
                            Console.Error.WriteLine($"{Constants.ErrorChar} Unsupported argument type '{argType}' in script '{scriptName}'.");
                            return Result.Error;
                    }
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

                    switch (argType) {
                        case "string":
                            scriptParams.Add(arg);
                            break;

                        case "stringArray":
                            scriptParams.Add(arg);
                            break;

                        case "number":
                            scriptParams.Add(Convert.ToDouble(arg));
                            break;

                        case "boolean":
                            scriptParams.Add(Convert.ToBoolean(arg));
                            break;

                        case "object":
                            scriptParams.Add(arg);
                            break;

                        default:
                            Console.Error.WriteLine($"{Constants.ErrorChar} Unsupported argument type '{argType}' in script '{scriptName}'.");
                            return Result.Error;
                    }
                }

                i++;
            }
        }

    if (Console.IsInputRedirected && i < argumentDefinitions.Count) {
            var argString = Console.In.ReadToEnd().Trim();
            var argType = argumentDefinitions[i].Type;

            if (argType == "string") {
                scriptParams.Add(argString);
            }
            else if (argType == "stringArray") {
                scriptParams.Add(argString);
            }
            else if (argType == "number") {
                scriptParams.Add(Convert.ToDouble(argString));
            }
            else if (argType == "boolean") {
                scriptParams.Add(Convert.ToBoolean(argString));
            }
            else if (argType == "object") {
                scriptParams.Add(argString);
            }
            else {
                scriptParams.Add(argString);
            }

            ++i;
        }

        // Apply default values for any remaining arguments not provided
        while (i < argumentDefinitions.Count) {
            var argDef = argumentDefinitions[i];
            if (argDef.Default is not null) {
                var argType = argDef.Type;
                var defVal = argDef.Default;
                switch (argType) {
                    case "string":
                        scriptParams.Add(defVal?.ToString());
                        break;
                    case "number":
                        scriptParams.Add(Convert.ToDouble(defVal));
                        break;
                    case "boolean":
                        scriptParams.Add(Convert.ToBoolean(defVal));
                        break;
                    case "object":
                    case "stringArray":
                        scriptParams.Add(defVal);
                        break;
                    default:
                        scriptParams.Add(defVal);
                        break;
                }
            } else {
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
            dynamic scriptRoot = _scriptEngine.Script;
            dynamic a2c = scriptRoot.a2c;

            if (string.IsNullOrEmpty(workspaceName)) {
                var cacheKey = $"js:root:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] cache hit: {cacheKey}");
                    }
                    CommandResult = cachedFunc.Invoke(false, scriptParams.ToArray());
                    invokedViaReference = true;
                } else {
                // Lookup function at a2c.{scriptName}
                object? func = null;
                if (a2c is IDictionary<string, object?> a2cDict) {
                    a2cDict.TryGetValue(scriptName, out func);
                } else {
                    // Try dynamic access
                    try { func = ((object)a2c).GetType().GetProperty(scriptName)?.GetValue(a2c); } catch { func = null; }
                }

                if (func is Microsoft.ClearScript.ScriptObject so) {
                    CommandResult = so.Invoke(false, scriptParams.ToArray());
                    _jsFuncCache.TryAdd(cacheKey, so);
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] cache add: {cacheKey}");
                    }
                    invokedViaReference = true;
                }
                }
            }
            else {
                var cacheKey = $"js:ws:{workspaceName}:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] cache hit: {cacheKey}");
                    }
                    CommandResult = cachedFunc.Invoke(false, scriptParams.ToArray());
                    invokedViaReference = true;
                } else {
                // Lookup function at a2c.workspaces.{workspaceName}.{scriptName}
                object? func = null;
                object? wsObj = null;

                // Workspaces is an ExpandoObject set up in ClearScript
                if (a2c.Workspaces is IDictionary<string, object?> workspaces && workspaces.TryGetValue(workspaceName, out wsObj) && wsObj is not null) {
                    if (wsObj is IDictionary<string, object?> wsDict) {
                        wsDict.TryGetValue(scriptName, out func);
                    } else {
                        try { func = wsObj.GetType().GetProperty(scriptName)?.GetValue(wsObj); } catch { func = null; }
                    }
                }

                if (func is Microsoft.ClearScript.ScriptObject so) {
                    // Workspace wrapper already injects the workspace, so pass user args only
                    CommandResult = so.Invoke(false, scriptParams.ToArray());
                    _jsFuncCache.TryAdd(cacheKey, so);
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] cache add: {cacheKey}");
                    }
                    invokedViaReference = true;
                }
                }
            }
        }
        catch { /* Swallow and fall back to name-based invocation below */ }

        if (!invokedViaReference) {
            // Fallback to existing name-based invocation (__script__...)
            string scriptBody = string.Empty;

            if (string.IsNullOrEmpty(workspaceName)) {
                found = _workspaceService.BaseConfig.Scripts.TryGetValue(scriptName, out scriptDefinition);

                if (!found) {
                    Console.Error.WriteLine($"{Constants.ErrorChar} Script '{scriptName}' not found.");
                    return Result.Error;
                }

                scriptBody = $"__script__{scriptName}";
                if (IsScriptDebugEnabled()) {
                    Console.Error.WriteLine($"[script-debug] fallback invoke: {scriptBody}");
                }
                try {
                    CommandResult = _scriptEngine.Invoke(scriptBody, scriptParams.ToArray());
                    // Promote: attempt to resolve and cache a2c.{scriptName} reference for next time
                    PromoteReferenceToCache(scriptName, null);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"{Constants.ErrorChar} Script '{scriptName}' (language: {resolvedLanguage}) could not be invoked. It does not appear to be registered as a function.\n- Check the script's language tag and that initialization completed.\n- If this is a workspace script, ensure the workspace name is correct.\nDetails: {ex.Message}");
                    return Result.Error;
                }
            }
            else {
                if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var workspace)) {
                    found = workspace.Scripts.TryGetValue(scriptName, out scriptDefinition);

                    if (!found) {
                        Console.Error.WriteLine($"{Constants.ErrorChar} Script '{workspaceName}.{scriptName}' not found.");
                        return Result.Error;
                    }

                    scriptBody = $"__script__{workspaceName}__{scriptName}";
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] fallback invoke: {scriptBody}");
                    }

                    // For the __script__ workspace function, inject the workspace as the first arg
                    var argsWithWorkspace = new List<object?>();
                    if (workspaceObjForFallback is not null) {
                        argsWithWorkspace.Add(workspaceObjForFallback);
                    }
                    argsWithWorkspace.AddRange(scriptParams);

                    try {
                        CommandResult = _scriptEngine.Invoke(scriptBody, argsWithWorkspace.ToArray());
                        // Promote: attempt to resolve and cache a2c.workspaces.{workspace}.{scriptName} reference for next time
                        PromoteReferenceToCache(scriptName, workspaceName);
                    }
                    catch (Exception ex) {
                        Console.Error.WriteLine($"{Constants.ErrorChar} Script '{workspaceName}.{scriptName}' (language: {resolvedLanguage}) could not be invoked. It does not appear to be registered as a function.\n- Check the script's language tag and that initialization completed.\n- Verify the workspace name and that the script is under that workspace.\nDetails: {ex.Message}");
                        return Result.Error;
                    }
                }
                else {
                    Console.Error.WriteLine($"{Constants.ErrorChar} Workspace '{workspaceName}' not found.");
                    return Result.Error;
                }
            }
        }
        return Result.Success;
    }

    // After a successful fallback invocation, try to resolve the canonical a2c.* reference and cache it.
    private void PromoteReferenceToCache(string scriptName, string? workspaceName)
    {
        try {
            dynamic scriptRoot = _scriptEngine.Script;
            dynamic a2c = scriptRoot.a2c;

            if (string.IsNullOrEmpty(workspaceName)) {
                var cacheKey = $"js:root:{scriptName}";
                if (_jsFuncCache.ContainsKey(cacheKey)) {
                    return;
                }
                object? func = null;
                if (a2c is IDictionary<string, object?> a2cDict) {
                    a2cDict.TryGetValue(scriptName, out func);
                } else {
                    try { func = ((object)a2c).GetType().GetProperty(scriptName)?.GetValue(a2c); } catch { func = null; }
                }
                if (func is Microsoft.ClearScript.ScriptObject so) {
                    _jsFuncCache.TryAdd(cacheKey, so);
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] promote cache add: {cacheKey}");
                    }
                }
            } else {
                var cacheKey = $"js:ws:{workspaceName}:{scriptName}";
                if (_jsFuncCache.ContainsKey(cacheKey)) {
                    return;
                }
                object? wsObj = null;
                object? func = null;
                if (a2c.Workspaces is IDictionary<string, object?> workspaces && workspaces.TryGetValue(workspaceName, out wsObj) && wsObj is not null) {
                    if (wsObj is IDictionary<string, object?> wsDict) {
                        wsDict.TryGetValue(scriptName, out func);
                    } else {
                        try { func = wsObj.GetType().GetProperty(scriptName)?.GetValue(wsObj); } catch { func = null; }
                    }
                }
                if (func is Microsoft.ClearScript.ScriptObject so) {
                    _jsFuncCache.TryAdd(cacheKey, so);
                    if (IsScriptDebugEnabled()) {
                        Console.Error.WriteLine($"[script-debug] promote cache add: {cacheKey}");
                    }
                }
            }
        }
        catch { /* ignore promotion errors */ }
    }

    // Probe whether a script is registered and where. Returns true if resolvable at runtime.
    // location: one of "root:a2c", "workspace:a2c", "root:__script__", "workspace:__script__", or "none".
    public bool TryResolveScriptFunction(string scriptName, string? workspaceName, out string location, out string language)
    {
        location = "none";
        language = "javascript";

        if (string.IsNullOrEmpty(scriptName)) {
            return false;
        }

        // Get definition and language
        ScriptDefinition? scriptDefinition = null;
        if (string.IsNullOrEmpty(workspaceName)) {
            _workspaceService.BaseConfig.Scripts.TryGetValue(scriptName, out scriptDefinition);
        } else if (_workspaceService.BaseConfig.Workspaces.TryGetValue(workspaceName, out var ws)) {
            ws.Scripts.TryGetValue(scriptName, out scriptDefinition);
        }

        if (scriptDefinition is not null) {
            try { language = scriptDefinition.ScriptTags?.FirstOrDefault() ?? "javascript"; } catch { /* ignore */ }
        }

        // JS path: attempt direct a2c.* resolution
        try {
            dynamic scriptRoot = _scriptEngine.Script;
            dynamic a2c = scriptRoot.a2c;

            if (string.IsNullOrEmpty(workspaceName)) {
                var cacheKey = $"js:root:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                    location = "root:a2c";
                    return true;
                }
                object? func = null;
                if (a2c is IDictionary<string, object?> a2cDict) {
                    a2cDict.TryGetValue(scriptName, out func);
                } else {
                    try { func = ((object)a2c).GetType().GetProperty(scriptName)?.GetValue(a2c); } catch { func = null; }
                }
                if (func is Microsoft.ClearScript.ScriptObject so) {
                    _jsFuncCache.TryAdd(cacheKey, so);
                    location = "root:a2c";
                    return true;
                }
            } else {
                var cacheKey = $"js:ws:{workspaceName}:{scriptName}";
                if (_jsFuncCache.TryGetValue(cacheKey, out var cachedFunc)) {
                    location = "workspace:a2c";
                    return true;
                }
                object? func = null;
                object? wsObj = null;
                if (a2c.Workspaces is IDictionary<string, object?> workspaces && workspaces.TryGetValue(workspaceName, out wsObj) && wsObj is not null) {
                    if (wsObj is IDictionary<string, object?> wsDict) {
                        wsDict.TryGetValue(scriptName, out func);
                    } else {
                        try { func = wsObj.GetType().GetProperty(scriptName)?.GetValue(wsObj); } catch { func = null; }
                    }
                }
                if (func is Microsoft.ClearScript.ScriptObject so) {
                    _jsFuncCache.TryAdd(cacheKey, so);
                    location = "workspace:a2c";
                    return true;
                }
            }
        }
        catch { /* ignore and try fallback */ }

        // Fallback: check for __script__ function existence using a light eval (JS only)
        try {
            if (string.IsNullOrEmpty(workspaceName)) {
                var test = _scriptEngine.EvaluateScript($"typeof __script__{scriptName} === 'function'");
                if (Convert.ToBoolean(test)) { location = "root:__script__"; return true; }
            } else {
                var test = _scriptEngine.EvaluateScript($"typeof __script__{workspaceName}__{scriptName} === 'function'");
                if (Convert.ToBoolean(test)) { location = "workspace:__script__"; return true; }
            }
        }
        catch { /* ignore */ }

        return false;
    }
}
