using System;
using System.Linq;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Runtime.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Commands; // for RunWsScriptCommand cache clear
using static ParksComputing.Api2Cli.Scripting.Services.ScriptEngineKinds;

namespace ParksComputing.Api2Cli.Runtime.Services.Impl;

public class ScriptRuntimeInitializer : IScriptRuntimeInitializer
{
    public void Initialize(A2CApi a2c, IWorkspaceService workspaceService, IApi2CliScriptEngineFactory engineFactory)
    {
    var jsScriptEngine = engineFactory.GetEngine(JavaScript);
        jsScriptEngine.InitializeScriptEnvironment();
        // Initialize C# only when needed, to avoid Roslyn startup cost on JS-only scenarios
        bool forceCs = string.Equals(Environment.GetEnvironmentVariable("A2C_FORCE_CS"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("A2C_FORCE_CS"), "1", StringComparison.OrdinalIgnoreCase);

        if (forceCs || HasAnyCSharp(workspaceService)) {
            var csScriptEngine = engineFactory.GetEngine(CSharp);
            csScriptEngine.InitializeScriptEnvironment();
        }

        // Clear cached JS function refs
        RunWsScriptCommand.ClearJsFunctionCache();

        // Optional warmup (controlled by env vars)
        var warmupFlag = Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP");
        var doWarmup = string.Equals(warmupFlag, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(warmupFlag, "1", StringComparison.OrdinalIgnoreCase);

        if (doWarmup) {
            int limit = 25;
            var limitStr = Environment.GetEnvironmentVariable("A2C_SCRIPT_WARMUP_LIMIT");

            if (!string.IsNullOrEmpty(limitStr) && int.TryParse(limitStr, out var parsed) && parsed > 0)
            {
                limit = parsed;
            }

            var resolver = new RunWsScriptCommand(workspaceService, engineFactory, jsScriptEngine);
            int warmed = 0;

            foreach (var kvp in workspaceService.BaseConfig.Scripts)
            {
                if (warmed >= limit) {
                    break;
                }

                var name = kvp.Key;
                var def = kvp.Value;
                var lang = def.ScriptTags?.FirstOrDefault() ?? JavaScript;

                if (!string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                resolver.TryResolveScriptFunction(name, null, out _, out _);
                warmed++;
            }

            if (warmed < limit)
            {
                foreach (var wkvp in workspaceService.BaseConfig.Workspaces) {
                    if (warmed >= limit) {
                        break;
                    }
                    var wsName = wkvp.Key;
                    var ws = wkvp.Value;

                    foreach (var skvp in ws.Scripts) {
                        if (warmed >= limit) {
                            break;
                        }

                        var name = skvp.Key;
                        var def = skvp.Value;
                        var lang = def.ScriptTags?.FirstOrDefault() ?? JavaScript;

                        if (!string.Equals(lang, JavaScript, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        resolver.TryResolveScriptFunction(name, wsName, out _, out _);
                        warmed++;
                    }
                }
            }

            var dbg = Environment.GetEnvironmentVariable("A2C_SCRIPT_DEBUG");

            if (string.Equals(dbg, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(dbg, "1", StringComparison.OrdinalIgnoreCase)) {

            }
        }
    }

    private static bool HasAnyCSharp(IWorkspaceService workspaceService) {
        var bc = workspaceService.BaseConfig;
        bool anyCsScripts = bc.Scripts.Any(s => string.Equals(s.Value.ScriptTags?.FirstOrDefault() ?? JavaScript, CSharp, StringComparison.OrdinalIgnoreCase))
            || bc.Workspaces.Any(ws => ws.Value.Scripts.Any(s => string.Equals(s.Value.ScriptTags?.FirstOrDefault() ?? JavaScript, CSharp, StringComparison.OrdinalIgnoreCase)));
        bool anyCsInit = (bc.ScriptInit?.CSharp is string csInit && !string.IsNullOrWhiteSpace(csInit))
            || (bc.InitScript?.Keys?.FirstOrDefault() is string gk && string.Equals(gk, CSharp, StringComparison.OrdinalIgnoreCase))
            || bc.Workspaces.Any(ws => (ws.Value.ScriptInit?.CSharp is string wcs && !string.IsNullOrWhiteSpace(wcs))
                || (ws.Value.InitScript?.Keys?.FirstOrDefault() is string wk && string.Equals(wk, CSharp, StringComparison.OrdinalIgnoreCase)));
        bool anyCsHandlers = (bc.PreRequest?.Keys?.FirstOrDefault() is string pr && string.Equals(pr, CSharp, StringComparison.OrdinalIgnoreCase))
            || (bc.PostResponse?.Keys?.FirstOrDefault() is string po && string.Equals(po, CSharp, StringComparison.OrdinalIgnoreCase))
            || bc.Workspaces.Any(w => (w.Value.PreRequest?.Keys?.FirstOrDefault() is string wpr && string.Equals(wpr, CSharp, StringComparison.OrdinalIgnoreCase))
                || (w.Value.PostResponse?.Keys?.FirstOrDefault() is string wpo && string.Equals(wpo, CSharp, StringComparison.OrdinalIgnoreCase))
                || w.Value.Requests.Any(r => (r.Value.PreRequest?.Keys?.FirstOrDefault() is string rpr && string.Equals(rpr, CSharp, StringComparison.OrdinalIgnoreCase))
                    || (r.Value.PostResponse?.Keys?.FirstOrDefault() is string rpo && string.Equals(rpo, CSharp, StringComparison.OrdinalIgnoreCase))));
        return anyCsScripts || anyCsInit || anyCsHandlers;
    }
}
