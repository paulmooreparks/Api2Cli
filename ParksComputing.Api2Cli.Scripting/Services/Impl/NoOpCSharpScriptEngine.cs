using System;
using System.Dynamic;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl {
    /// <summary>
    /// Minimal no-op implementation of IApi2CliScriptEngine for C#.
    /// Useful for performance A/B testing by stubbing out Roslyn costs.
    /// </summary>
    internal sealed class NoOpScriptEngine : IApi2CliScriptEngine {
        private dynamic _globals = new ExpandoObject();

        public dynamic Script => _globals;

        public void InitializeScriptEnvironment() { /* no-op */ }

        public void SetValue(string name, object? value) {
            ((IDictionary<string, object?>)_globals)[name] = value;
        }

        public string ExecuteScript(string? script) => string.Empty;

        public object? EvaluateScript(string? script) => null;

        public string ExecuteCommand(string? script) => string.Empty;

        public void InvokePreRequest(params object?[] args) { /* no-op */ }

        public object? InvokePostResponse(params object?[] args) => null;

        public object? Invoke(string script, params object?[] args) => null;

        public void AddHostObject(string itemName, object? target) {
            if (target is null) {
                return;
            }
            SetValue(itemName, target);
        }

        public void ExecuteInitScript(string? script) { /* no-op */ }

        public void ExecuteInitScript(ParksComputing.Xfer.Lang.XferKeyedValue? script) { /* no-op */ }

        public void ExecuteAllWorkspaceInitScripts() { /* no-op */ }

    public void EnsureWorkspaceProjected(string workspaceName) { /* no-op */ }

    public void ExecuteWorkspaceInitFor(string workspaceName) { /* no-op */ }

            public void Reset() {
                // Recreate globals so any previously set variables are cleared
                _globals = new ExpandoObject();
            }
    }
}
