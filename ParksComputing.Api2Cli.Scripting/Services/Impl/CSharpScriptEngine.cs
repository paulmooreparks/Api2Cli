using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;
using Microsoft.CodeAnalysis;
using System.IO;
using System.Collections.Immutable;

using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Workspace.Models;
using System.Net.Http.Headers;
using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Scripting.Extensions;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl {
    internal class CSharpScriptEngine : IApi2CliScriptEngine {
        private readonly IPackageService _packageService;
        private readonly IWorkspaceService _workspaceService;
        private readonly ISettingsService _settingsService;
        private readonly IAppDiagnostics<CSharpScriptEngine> _diags;
        private readonly IPropertyResolver _propertyResolver;
        private readonly A2CApi _a2c;

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
            A2CApi apiRoot
        ) {
            _packageService = packageService;
            _workspaceService = workspaceService;
            _settingsService = settingsService;
            _diags = appDiagnostics;
            _propertyResolver = propertyResolver;
            _a2c = apiRoot;

            // Create ScriptOptions with proper single-file application support
            _options = CreateScriptOptions();

            // Add host objects to the script globals
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("a2c", _a2c); // Ensure a2c is added to the script globals
        }

        private ScriptOptions CreateScriptOptions()
        {
            var references = new List<MetadataReference>();

            // Core assemblies to include
            var coreAssemblies = new[]
            {
                typeof(object).Assembly,           // System.Runtime
                typeof(System.Linq.Enumerable).Assembly  // System.Linq
            };

            // Try to create references from assemblies
            foreach (var assembly in coreAssemblies)
            {
                try
                {
                    var reference = CreateMetadataReferenceFromAssembly(assembly);
                    if (reference != null)
                    {
                        references.Add(reference);
                    }
                }
                catch (Exception ex)
                {
                    // Log the specific assembly that failed, but continue with others
                    _diags.Emit(nameof(CSharpScriptEngine), new { 
                        Message = $"Could not create reference for {assembly.FullName}: {ex.Message}",
                        Assembly = assembly.FullName
                    });
                }
            }

            return ScriptOptions.Default
                .WithReferences(references)
                .WithImports("System", "System.Linq", "System.Collections.Generic");
        }

        private MetadataReference? CreateMetadataReferenceFromAssembly(Assembly assembly)
        {
            try
            {
                // First try the standard approach (works for non-single-file apps)
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    return MetadataReference.CreateFromFile(assembly.Location);
                }

                // For single-file apps, create reference from assembly image bytes
                var assemblyBytes = GetAssemblyBytes(assembly);
                if (assemblyBytes != null)
                {
                    return MetadataReference.CreateFromImage(assemblyBytes);
                }
            }
            catch (NotSupportedException)
            {
                // Expected for single-file applications when trying to use Assembly.Location
            }
            catch (Exception ex)
            {
                _diags.Emit(nameof(CSharpScriptEngine), new { 
                    Message = $"Failed to create metadata reference for {assembly.FullName}: {ex.Message}"
                });
            }

            return null;
        }

        private ImmutableArray<byte>? GetAssemblyBytes(Assembly assembly)
        {
            try
            {
                // Try to get the assembly bytes from the loaded assembly
                // This approach works for both regular and single-file applications
                var assemblyName = assembly.GetName();
                
                // For system assemblies, we can often find them in the runtime directory
                var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                if (!string.IsNullOrEmpty(runtimeDir))
                {
                    var potentialPath = Path.Combine(runtimeDir, $"{assemblyName.Name}.dll");
                    if (File.Exists(potentialPath))
                    {
                        var bytes = File.ReadAllBytes(potentialPath);
                        return ImmutableArray.Create(bytes);
                    }
                }

                // Alternative approach: try to read from embedded location if available
                if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
                {
                    var bytes = File.ReadAllBytes(assembly.Location);
                    return ImmutableArray.Create(bytes);
                }
            }
            catch (Exception ex)
            {
                _diags.Emit(nameof(CSharpScriptEngine), new { 
                    Message = $"Could not read assembly bytes for {assembly.FullName}: {ex.Message}"
                });
            }

            return null;
        }

        public dynamic Script => _scriptGlobals;

        public void InitializeScriptEnvironment() {
            _scriptGlobals = new ExpandoObject();
            _globals.Clear();
            _state = null;

            // Re-add host objects after reinitialization
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("a2c", _a2c); // Ensure a2c is re-added to the script globals
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
