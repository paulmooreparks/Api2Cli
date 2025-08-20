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
using ParksComputing.Xfer.Lang;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl {
    internal class CSharpScriptEngine : IApi2CliScriptEngine {
    private readonly IPackageService _packageService = null!;
    private readonly IWorkspaceService _workspaceService = null!;
    private readonly ISettingsService _settingsService = null!;
    private readonly IAppDiagnostics<CSharpScriptEngine> _diags = null!;
    private readonly IPropertyResolver _propertyResolver = null!;
    private readonly A2CApi _a2c = null!;

    private ScriptOptions _options = null!;
        private ScriptState<object?>? _state;
        private readonly Dictionary<string, object?> _globals = new();
        private dynamic _scriptGlobals = new ExpandoObject();

    // Strongly-typed globals object so C# scripts can reference members like 'a2c'
    private ScriptGlobals _typedGlobals;

        public CSharpScriptEngine(
            IPackageService packageService,
            IWorkspaceService workspaceService,
            ISettingsService settingsService,
            IAppDiagnostics<CSharpScriptEngine> appDiagnostics,
            IPropertyResolver propertyResolver,
            A2CApi apiRoot
        ) {
            _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _diags = appDiagnostics ?? throw new ArgumentNullException(nameof(appDiagnostics));
            _propertyResolver = propertyResolver ?? throw new ArgumentNullException(nameof(propertyResolver));
            _a2c = apiRoot ?? throw new ArgumentNullException(nameof(apiRoot));

            // React to NuGet package updates (install/uninstall) by refreshing script references
            if (_packageService is not null)
            {
                _packageService.PackagesUpdated += OnPackagesUpdated;
            }

            // Create ScriptOptions with proper single-file application support
            _options = CreateScriptOptions();

            // Initialize typed globals
            _typedGlobals = new ScriptGlobals { a2c = _a2c, a2cjs = new CaseInsensitiveDynamicProxy(_a2c) };

            // Add host objects to the script globals (for any dynamic access patterns)
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("a2c", _a2c); // Ensure a2c is available; typed via _typedGlobals
            AddHostObject("a2cjs", new CaseInsensitiveDynamicProxy(_a2c));
            AddHostObject("console", typeof(ConsoleScriptObject));
        }

        private ScriptOptions CreateScriptOptions()
        {
            var references = new List<MetadataReference>();

            // Core assemblies to include
            var coreAssemblies = new[]
            {
                typeof(object).Assembly,                      // System.Runtime
                typeof(System.Linq.Enumerable).Assembly,      // System.Linq
                typeof(Console).Assembly,                     // System.Console
                typeof(Task).Assembly,                        // System.Threading.Tasks
                typeof(System.Dynamic.ExpandoObject).Assembly,// System.Dynamic.Runtime
                typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly, // Microsoft.CSharp (dynamic binder)
                typeof(Uri).Assembly,                         // System.Private.Uri (Uri)
                typeof(System.Net.Http.HttpClient).Assembly,  // System.Net.Http (HttpResponseHeaders, HttpClient)
                typeof(A2CApi).Assembly,                      // Api root type assembly
                typeof(ScriptGlobals).Assembly,                // Globals container assembly
                typeof(System.Text.Json.JsonSerializer).Assembly // System.Text.Json for JSON conversions
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

            // Add references for any installed NuGet package assemblies under .a2c/packages
            try
            {
                AddPackageAssemblyReferences(references);
            }
            catch (Exception ex)
            {
                _diags.Emit(nameof(CSharpScriptEngine), new {
                    Message = $"Failed to add package assembly references: {ex.Message}",
                });
            }

            // Add references for any assemblies configured in the workspace (plugins/packages)
            try
            {
                AddWorkspaceAssemblyReferences(references);
            }
            catch (Exception ex)
            {
                _diags.Emit(nameof(CSharpScriptEngine), new {
                    Message = $"Failed to add workspace assembly references: {ex.Message}",
                });
            }

            return ScriptOptions.Default
                .WithReferences(references)
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Threading.Tasks",
                    "System.Dynamic",
                    "System.Text.Json"
                );
        }

        private void AddPackageAssemblyReferences(List<MetadataReference> references)
        {
            var dllPaths = _packageService?.GetInstalledPackagePaths();
            if (dllPaths == null)
            {
                return;
            }

            // Track existing file names to avoid duplicates
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in references)
            {
                if (r is PortableExecutableReference per && per.FilePath is string p && !string.IsNullOrEmpty(p))
                {
                    existing.Add(Path.GetFileName(p));
                }
            }

            foreach (var path in dllPaths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }
                    var fileName = Path.GetFileName(path);
                    if (existing.Contains(fileName))
                    {
                        continue;
                    }
                    references.Add(MetadataReference.CreateFromFile(path));
                    existing.Add(fileName);
                }
                catch (Exception ex)
                {
                    _diags.Emit(nameof(CSharpScriptEngine), new {
                        Message = $"Failed to reference package assembly '{path}': {ex.Message}"
                    });
                }
            }
        }

        private void OnPackagesUpdated()
        {
            // Rebuild ScriptOptions so new/removed packages are reflected in Roslyn references
            _options = CreateScriptOptions();
        }

        private void AddWorkspaceAssemblyReferences(List<MetadataReference> references)
        {
            var assemblies = _workspaceService?.BaseConfig?.Assemblies;
            if (assemblies == null || assemblies.Length == 0)
            {
                return;
            }

            // Build a set of existing reference file names to avoid duplicates
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in references)
            {
                if (r is PortableExecutableReference per && per.FilePath is string p && !string.IsNullOrEmpty(p))
                {
                    existing.Add(Path.GetFileName(p));
                }
            }

            string? pluginDir = null;
            try { pluginDir = _settingsService?.PluginDirectory; } catch { /* ignore */ }

            foreach (var name in assemblies)
            {
                try
                {
                    var path = name;
                    if (!Path.IsPathRooted(path))
                    {
                        if (!string.IsNullOrEmpty(pluginDir))
                        {
                            path = Path.Combine(pluginDir!, name);
                        }
                    }

                    if (!File.Exists(path))
                    {
                        _diags.Emit(nameof(CSharpScriptEngine), new {
                            Message = $"Workspace assembly not found: {path}"
                        });
                        continue;
                    }

                    var fileName = Path.GetFileName(path);
                    if (existing.Contains(fileName))
                    {
                        // Already referenced
                        continue;
                    }

                    var reference = MetadataReference.CreateFromFile(path);
                    references.Add(reference);
                    existing.Add(fileName);
                }
                catch (Exception ex)
                {
                    _diags.Emit(nameof(CSharpScriptEngine), new {
                        Message = $"Failed to reference workspace assembly '{name}': {ex.Message}"
                    });
                }
            }
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

            // Reset typed globals
            _typedGlobals = new ScriptGlobals { a2c = _a2c, a2cjs = new CaseInsensitiveDynamicProxy(_a2c) };

            // Rebuild ScriptOptions to pick up any newly configured workspace assemblies
            _options = CreateScriptOptions();

            // Re-add host objects after reinitialization
            AddHostObject("Console", typeof(Console));
            AddHostObject("Task", typeof(Task));
            AddHostObject("a2c", _a2c); // Ensure a2c is re-added to the script globals
            AddHostObject("a2cjs", new CaseInsensitiveDynamicProxy(_a2c));
        }

        public void SetValue(string name, object? value) {
            ((IDictionary<string, object?>) _scriptGlobals)[name] = value;
            _globals[name] = value;

            // Keep typed globals synchronized for well-known names so scripts can access them as identifiers
            switch (name)
            {
                case "a2c" when value is A2CApi a2c:
                    _typedGlobals.a2c = a2c;
                    break;
                case "a2cjs":
                    _typedGlobals.a2cjs = value!;
                    break;
                case "workspace":
                    _typedGlobals.workspace = value;
                    break;
                case "request":
                    _typedGlobals.request = value;
                    break;
            }
        }

        public string ExecuteScript(string? script) {
            if (string.IsNullOrWhiteSpace(script)) {
                return string.Empty;
            }

            // Execute the script synchronously; chain onto existing state so top-level defs persist
            if (_state is null)
            {
                _state = CSharpScript.RunAsync(script, _options, _typedGlobals, typeof(ScriptGlobals)).GetAwaiter().GetResult();
            }
            else
            {
                _state = _state.ContinueWithAsync(script, _options).GetAwaiter().GetResult();
            }
            return _state?.ReturnValue?.ToString() ?? string.Empty;
        }

        public object? EvaluateScript(string? script) {
            if (string.IsNullOrWhiteSpace(script)) {
                return null;
            }

            // Evaluate the script synchronously; chain onto existing state so earlier defs are visible
            if (_state is null)
            {
                _state = CSharpScript.RunAsync(script, _options, _typedGlobals, typeof(ScriptGlobals)).GetAwaiter().GetResult();
            }
            else
            {
                _state = _state.ContinueWithAsync(script, _options).GetAwaiter().GetResult();
            }
            return _state?.ReturnValue;
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
                try {
                    var parameters = d.Method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsArray && parameters[0].ParameterType.GetElementType() == typeof(object)) {
                        // Target delegate expects a single object[]; wrap args accordingly
                        return d.DynamicInvoke(new object?[] { args });
                    }
                    return d.DynamicInvoke(args);
                } catch (Exception ex) {
                    // Surface the inner exception (actual script failure) when present
                    var inner = ex.InnerException ?? ex;
                    var msg = $"{inner.GetType().FullName}: {inner.Message}";
                    _diags.Emit("InvokeError", new { Script = script, Message = msg, StackTrace = inner.StackTrace });
                    throw new InvalidOperationException($"CSharp script '{script}' failed: {msg}", inner);
                }
            }
            return null;
        }

        public void AddHostObject(string itemName, object? target) {
            if (target != null) {
                SetValue(itemName, target);

                // Keep typed globals in sync for well-known members
                if (string.Equals(itemName, "a2c", StringComparison.OrdinalIgnoreCase) && target is A2CApi a2c)
                {
                    _typedGlobals.a2c = a2c;
                }
            }
        }

        public void ExecuteInitScript(string? script) {
            if (string.IsNullOrWhiteSpace(script)) { return; }
            ExecuteScript(script);
        }

        // Overload for keyed value scenarios (initScript on workspace/baseConfig)
    public void ExecuteInitScript(XferKeyedValue? script) {
            var body = GetInitBodyForLanguage(script, ScriptEngineKinds.CSharp);
            if (string.IsNullOrWhiteSpace(body)) { return; }
            ExecuteScript(body);
        }

    private static string GetInitBodyForLanguage(XferKeyedValue? kv, string language)
        {
            // Return the script body exactly as provided in the configuration.
            // Honor keyed language (defaulting to javascript when missing) and support cs/csharp aliases.
            if (kv is null) {
                return string.Empty;
            }

            var body = kv.PayloadAsString ?? string.Empty;

            if (string.IsNullOrEmpty(body)) {
                return string.Empty;
            }

            var lang = kv.Keys?.FirstOrDefault();

            bool matches =
                string.Equals(lang, language, StringComparison.OrdinalIgnoreCase)
                || (lang != null && ScriptEngineKinds.CSharpAliases.Contains(lang, StringComparer.OrdinalIgnoreCase));

            return matches ? body : string.Empty;
        }

    // No-op for C# engine; workspace init ordering is managed by the orchestrator and JS engine
    public void ExecuteAllWorkspaceInitScripts() { }

    // Lazy activation hooks are not applicable to the C# engine
    public void EnsureWorkspaceProjected(string workspaceName) { }
    public void ExecuteWorkspaceInitFor(string workspaceName) { }

    }
}
