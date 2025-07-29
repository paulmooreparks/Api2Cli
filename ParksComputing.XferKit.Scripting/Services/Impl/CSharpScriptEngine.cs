using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

using ParksComputing.XferKit.Workspace.Services;
using ParksComputing.XferKit.Workspace.Models;
using System.Net.Http.Headers;
using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Diagnostics.Services;
using ParksComputing.XferKit.Workspace;
using ParksComputing.XferKit.Scripting.Extensions;

namespace ParksComputing.XferKit.Scripting.Services.Impl {
    internal class CSharpScriptEngine : IXferScriptEngine {
        private readonly IPackageService _packageService;
        private readonly IWorkspaceService _workspaceService;
        private readonly ISettingsService _settingsService;
        private readonly IAppDiagnostics<CSharpScriptEngine> _diags;
        private readonly IPropertyResolver _propertyResolver;
        private readonly XferKitApi _xk;

        private ScriptOptions _options;
        private ScriptState<object?>? _state;
        private readonly Dictionary<string, object?> _globals = new();
        private dynamic _scriptGlobals = new ExpandoObject();

        public CSharpScriptEngine(
            IPackageService packageService,
            IWorkspaceService workspaceService,
            ISettingsService settingsService,
            IAppDiagnostics<CSharpScriptEngine> appDiagnostics,
            IPropertyResolver propertyResolver,
            XferKitApi apiRoot
        ) {
            _packageService = packageService;
            _workspaceService = workspaceService;
            _settingsService = settingsService;
            _diags = appDiagnostics;
            _propertyResolver = propertyResolver;
            _xk = apiRoot;

            _options = ScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(System.Linq.Enumerable).Assembly
                )
                .WithImports("System", "System.Linq", "System.Collections.Generic");

            // Add host objects to the script globals
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("xk", _xk); // Ensure xk is added to the script globals
        }

        public dynamic Script => _scriptGlobals;

        public void InitializeScriptEnvironment() {
            _scriptGlobals = new ExpandoObject();
            _globals.Clear();
            _state = null;

            // Re-add host objects after reinitialization
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("xk", _xk); // Ensure xk is re-added to the script globals
        }

        public void SetValue(string name, object? value) {
            ((IDictionary<string, object?>) _scriptGlobals)[name] = value;
            _globals[name] = value;
        }

        public string ExecuteScript(string? script) {
            if (string.IsNullOrWhiteSpace(script)) {
                return string.Empty;
            }

            // Execute the script synchronously
            _state = CSharpScript.RunAsync(script, _options, _scriptGlobals).GetAwaiter().GetResult();
            return _state?.ReturnValue?.ToString() ?? string.Empty;
        }

        public object? EvaluateScript(string? script) {
            if (string.IsNullOrWhiteSpace(script)) {
                return null;
            }

            // Evaluate the script synchronously
            var result = CSharpScript.EvaluateAsync<object?>(script, _options, _scriptGlobals).GetAwaiter().GetResult();
            return result;
        }

        public string ExecuteCommand(string? script) {
            return ExecuteScript(script);
        }

        public void InvokePreRequest(params object?[] args) {
            if (((IDictionary<string, object?>) _scriptGlobals).TryGetValue("PreRequest", out var func) && func is Delegate d) {
                d.DynamicInvoke(args);
            }
        }

        public object? InvokePostResponse(params object?[] args) {
            if (((IDictionary<string, object?>) _scriptGlobals).TryGetValue("PostResponse", out var func) && func is Delegate d) {
                return d.DynamicInvoke(args);
            }
            return null;
        }

        public object? Invoke(string script, params object?[] args) {
            if (((IDictionary<string, object?>) _scriptGlobals).TryGetValue(script, out var func) && func is Delegate d) {
                return d.DynamicInvoke(args);
            }
            return null;
        }

        public void AddHostObject(string itemName, object? target) {
            if (target != null) {
                SetValue(itemName, target);
            }
        }

        public void ExecuteInitScript(string? script) {
            if (!string.IsNullOrWhiteSpace(script)) {
                try {
                    ExecuteScript(script);
                } catch (Exception ex) {
                    _diags.Emit("InitScriptError", new { Message = ex.Message });
                }
            }
        }
    }
}
