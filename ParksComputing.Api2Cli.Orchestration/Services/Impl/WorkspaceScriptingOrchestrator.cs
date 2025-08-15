using System;
using System.Collections.Generic;
using System.Linq;

using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;
// Note: Do not depend on CLI layer here.

namespace ParksComputing.Api2Cli.Orchestration.Services.Impl;

internal class WorkspaceScriptingOrchestrator : IWorkspaceScriptingOrchestrator
{
    private readonly IApi2CliScriptEngineFactory _engineFactory;
    private readonly IWorkspaceService _workspaceService;

    public WorkspaceScriptingOrchestrator(IApi2CliScriptEngineFactory engineFactory, IWorkspaceService workspaceService)
    {
        _engineFactory = engineFactory;
        _workspaceService = workspaceService;
    }

    public void Initialize()
    {
        var js = _engineFactory.GetEngine("javascript");
        js.InitializeScriptEnvironment();

        var cs = _engineFactory.GetEngine("csharp");
        cs.InitializeScriptEnvironment();

    // CLI-level JS function cache is process-local; nothing to clear here.

        // Register root scripts into JS engine's a2c
        foreach (var script in _workspaceService.BaseConfig.Scripts) {
            var name = script.Key;
            var def = script.Value;
            var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
            if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) continue;
            var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));
            var body = def.Script ?? string.Empty;
            var source = $@"function __script__{name}({paramList}) {{
{body}
}};

a2c.{name} = __script__{name};";
            js.EvaluateScript(source);
        }

        // Register workspace scripts wrappers are created in scripting engine; assume a2c.workspaces exists
        foreach (var wkvp in _workspaceService.BaseConfig.Workspaces) {
            var wsName = wkvp.Key;
            var ws = wkvp.Value;
            foreach (var script in ws.Scripts) {
                var name = script.Key;
                var def = script.Value;
                var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
                if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) continue;
                var paramList = string.Join(", ", def.Arguments.Select(a => a.Value.Name ?? a.Key));
                var body = def.Script ?? string.Empty;
                var source = $@"function __script__{wsName}__{name}(workspace, {paramList}) {{
{body}
}};

a2c.workspaces['{wsName}']['{name}'] = (" + (string.IsNullOrEmpty(paramList) ? "" : paramList + ", ") + $"...rest) => __script__{wsName}__{name}(a2c.workspaces['{wsName}'], " + (string.IsNullOrEmpty(paramList) ? "" : paramList + ", ") + "...rest);";
                js.EvaluateScript(source);
            }
        }
    }

    public void Warmup(int limit = 25, bool enable = false, bool debug = false)
    {
        if (!enable) return;
        int warmed = 0;
        var js = _engineFactory.GetEngine("javascript");

        // Touch a limited number of JS script references to ensure they are defined
        foreach (var kvp in _workspaceService.BaseConfig.Scripts) {
            if (warmed >= limit) break;
            var def = kvp.Value;
            var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
            if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) continue;
            try { js.EvaluateScript($"void(a2c['{kvp.Key}'])"); } catch { /* ignore */ }
            warmed++;
        }
        if (warmed < limit) {
            foreach (var wkvp in _workspaceService.BaseConfig.Workspaces) {
                if (warmed >= limit) break;
                var wsName = wkvp.Key;
                foreach (var skvp in wkvp.Value.Scripts) {
                    if (warmed >= limit) break;
                    var def = skvp.Value;
                    var lang = def.ScriptTags?.FirstOrDefault() ?? "javascript";
                    if (!string.Equals(lang, "javascript", StringComparison.OrdinalIgnoreCase)) continue;
                    try { js.EvaluateScript($"void(a2c.workspaces['{wsName}']['{skvp.Key}'])"); } catch { /* ignore */ }
                    warmed++;
                }
            }
        }
        if (debug) {
            Console.Error.WriteLine($"[script-debug] warmup complete, warmed {warmed} scripts");
        }
    }
}
