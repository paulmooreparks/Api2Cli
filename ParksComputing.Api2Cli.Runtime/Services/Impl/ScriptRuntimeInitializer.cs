using System;
using System.Linq;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Runtime.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Commands; // for RunWsScriptCommand cache clear

namespace ParksComputing.Api2Cli.Runtime.Services.Impl;

public class ScriptRuntimeInitializer : IScriptRuntimeInitializer
{
    public void Initialize(A2CApi a2c, IWorkspaceService workspaceService, IApi2CliScriptEngineFactory engineFactory)
    {
        var jsScriptEngine = engineFactory.GetEngine("javascript");
        jsScriptEngine.InitializeScriptEnvironment();

        var csScriptEngine = engineFactory.GetEngine("csharp");
        csScriptEngine.InitializeScriptEnvironment();

        // Clear cached JS function refs
        RunWsScriptCommand.ClearJsFunctionCache();

        // Optional warmup (controlled by env vars)
        var warmupFlag = Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP");
        var doWarmup = string.Equals(warmupFlag, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(warmupFlag, "1", StringComparison.OrdinalIgnoreCase);
        if (doWarmup) {
            int limit = 25;
            var limitStr = Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP_LIMIT");
            if (!string.IsNullOrEmpty(limitStr) && int.TryParse(limitStr, out var parsed) && parsed > 0) {
                limit = parsed;
            }

            var resolver = new RunWsScriptCommand(workspaceService, engineFactory, jsScriptEngine);
            int warmed = 0;

            foreach (var kvp in workspaceService.BaseConfig.Scripts) {
                if (warmed >= limit) { break; }
                var name = kvp.Key;
                var def = kvp.Value;
                var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
                if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) { continue; }
                resolver.TryResolveScriptFunction(name, null, out _, out _);
                warmed++;
            }

            if (warmed < limit) {
                foreach (var wkvp in workspaceService.BaseConfig.Workspaces) {
                    if (warmed >= limit) { break; }
                    var wsName = wkvp.Key;
                    var ws = wkvp.Value;
                    foreach (var skvp in ws.Scripts) {
                        if (warmed >= limit) { break; }
                        var name = skvp.Key;
                        var def = skvp.Value;
                        var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
                        if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) { continue; }
                        resolver.TryResolveScriptFunction(name, wsName, out _, out _);
                        warmed++;
                    }
                }
            }

            var dbg = Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG");
            if (string.Equals(dbg, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(dbg, "1", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine($"[script-debug] warmup complete, warmed {warmed} scripts");
            }
        }
    }
}
