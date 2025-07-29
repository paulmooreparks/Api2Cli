using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ParksComputing.XferKit.Scripting.Services.Impl
{
    internal class CSharpScriptEngine : IXferScriptEngine
    {
        private ScriptOptions _options;
        private ScriptState<object?>? _state;
        private readonly Dictionary<string, object?> _globals = new();
        private dynamic _scriptGlobals = new ExpandoObject();

        public CSharpScriptEngine()
        {
            _options = ScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(System.Linq.Enumerable).Assembly
                )
                .WithImports("System", "System.Linq", "System.Collections.Generic");
        }

        public dynamic Script => _scriptGlobals;

        public void InitializeScriptEnvironment()
        {
            _scriptGlobals = new ExpandoObject();
            _globals.Clear();
            _state = null;
        }

        public void SetValue(string name, object? value)
        {
            ((IDictionary<string, object?>)_scriptGlobals)[name] = value;
            _globals[name] = value;
        }

        public string ExecuteScript(string? script)
        {
            if (string.IsNullOrWhiteSpace(script)) {
                return string.Empty;
            }

            _state = CSharpScript.RunAsync(script, _options, _scriptGlobals).Result;
            return _state?.ReturnValue?.ToString() ?? string.Empty;
        }

        public object? EvaluateScript(string? script)
        {
            if (string.IsNullOrWhiteSpace(script)) {
                return null;
            }

            var result = CSharpScript.EvaluateAsync<object?>(script, _options, _scriptGlobals).Result;
            return result;
        }

        public string ExecuteCommand(string? script)
        {
            return ExecuteScript(script);
        }

        public void InvokePreRequest(params object?[] args)
        {
            if (((IDictionary<string, object?>)_scriptGlobals).TryGetValue("PreRequest", out var func) && func is Delegate d)
            {
                d.DynamicInvoke(args);
            }
        }

        public object? InvokePostResponse(params object?[] args)
        {
            if (((IDictionary<string, object?>)_scriptGlobals).TryGetValue("PostResponse", out var func) && func is Delegate d)
            {
                return d.DynamicInvoke(args);
            }
            return null;
        }

        public object? Invoke(string script, params object?[] args)
        {
            if (((IDictionary<string, object?>)_scriptGlobals).TryGetValue(script, out var func) && func is Delegate d)
            {
                return d.DynamicInvoke(args);
            }
            return null;
        }

        public void AddHostObject(string itemName, object target)
        {
            SetValue(itemName, target);
        }
    }
}
