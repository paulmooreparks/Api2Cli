using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

using ParksComputing.Xfer.Lang.Configuration;
using ParksComputing.Xfer.Lang.Converters;

using ParksComputing.Xfer.Lang;
using ParksComputing.Xfer.Lang.Attributes;
using ParksComputing.Xfer.Lang.Elements;
using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Workspace.Services.Impl;

internal class WorkspaceService : IWorkspaceService {
    private readonly ISettingsService _settingsService;
    private readonly IAppDiagnostics<WorkspaceService> _diags;

    public BaseConfig BaseConfig { get; protected set; }
    public WorkspaceDefinition ActiveWorkspace { get; protected set; }
    public string WorkspaceFilePath { get; protected set; }
    private string ConfigRoot => Path.GetDirectoryName(WorkspaceFilePath) ?? _settingsService.Api2CliSettingsDirectory ?? string.Empty;
    public string CurrentWorkspaceName => BaseConfig?.ActiveWorkspace ?? string.Empty;

    private readonly string _packageDirectory;

    public IEnumerable<string> WorkspaceList {
        get {
            if (BaseConfig is not null && BaseConfig.Workspaces is not null) {
                return BaseConfig.Workspaces.Keys;
            }

            return new List<string>();
        }
    }

    public WorkspaceService(
        ISettingsService settingsService,
        IAppDiagnostics<WorkspaceService> appDiagnostics
        ) {
        // WorkspaceInitializer.InitializeWorkspace(settingsService);
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        LoadEnvironmentVariables(_settingsService.EnvironmentFilePath);
        _diags = appDiagnostics ?? throw new ArgumentNullException(nameof(appDiagnostics));

        ActiveWorkspace = new WorkspaceDefinition();
        WorkspaceFilePath = _settingsService.ConfigFilePath;
        _packageDirectory = _settingsService.PluginDirectory ?? string.Empty;

    EnsureConfigScaffold();
    BaseConfig = LoadWorkspace();
        LoadConfiguredAssemblies();
        SetActiveWorkspace(BaseConfig.ActiveWorkspace ?? string.Empty);

    }

    private void LoadEnvironmentVariables(string? environmentFilePath) {
        if (File.Exists(environmentFilePath)) {
            var lines = File.ReadAllLines(environmentFilePath);

            foreach (var line in lines) {
                var trimmedLine = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#')) {
                    continue;
                }

                var parts = trimmedLine.Split('=', 2);

                if (parts.Length == 2) {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim().Trim('"');
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }

    public void SetActiveWorkspace(string workspaceName) {
        if (!string.IsNullOrEmpty(workspaceName)) {
            if (BaseConfig.Workspaces is not null) {
                if (string.Equals(workspaceName, "/")) {
                    ActiveWorkspace = new WorkspaceDefinition();
                    BaseConfig.ActiveWorkspace = string.Empty;
                }
                else if (BaseConfig.Workspaces.TryGetValue(workspaceName, out WorkspaceDefinition? value)) {
                    if (value.IsHidden) {
                        throw new InvalidOperationException($"Workspace '{workspaceName}' is hidden and cannot be activated.");
                    }
                    ActiveWorkspace = value;
                    BaseConfig.ActiveWorkspace = workspaceName;

                    try {
                        ActiveWorkspaceChanged?.Invoke(workspaceName);
                    }
                    catch (Exception ex) {
                        // Surface but don't crash the selection change; listeners are user code
                        _diags.Emit(
                            nameof(IWorkspaceService),
                            new {
                                Message = $"ActiveWorkspaceChanged listener threw: {ex.Message}",
                                ex
                            }
                        );
                    }

                }
            }
        }
    }
    public event Action<string>? ActiveWorkspaceChanged;

    private void EnsureConfigScaffold() {
        // Create minimal config.xfer if missing; do not populate workspaces by default
        if (!File.Exists(WorkspaceFilePath)) {
            var baseConfig = new BaseConfig { Workspaces = new Dictionary<string, WorkspaceDefinition>() };
            var xfer = XferConvert.Serialize(baseConfig, Formatting.Indented | Formatting.Spaced);
            try {
                File.WriteAllText(WorkspaceFilePath, xfer, Encoding.UTF8);
            }
            catch (Exception ex) {
                throw new Exception($"Error creating config file '{WorkspaceFilePath}': {ex.Message}", ex);
            }
        }
        // Ensure workspaces directory exists
        var wsDir = Path.Combine(ConfigRoot, "workspaces");
        if (!Directory.Exists(wsDir)) {
            Directory.CreateDirectory(wsDir);
        }
    }

    /// <summary>
    /// Loads configuration from <root>/config.xfer and merges per-workspace files under <root>/workspaces/*/workspace.xfer.
    /// </summary>
    private BaseConfig LoadWorkspace() {
        var baseConfig = new BaseConfig();

        var timingsEnabled = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS"), "1", StringComparison.OrdinalIgnoreCase);
        Stopwatch? sw = null;
        if (timingsEnabled) {
            sw = Stopwatch.StartNew();
        }

    var xfer = File.ReadAllText(WorkspaceFilePath, Encoding.UTF8);
    var document = XferParser.Parse(xfer);

        if (document is null) {
            throw new Exception($"Error parsing workspace file '{WorkspaceFilePath}'.");
        }

        ObjectElement? baseConfigElement = null;

        if (document.Root is ObjectElement objectElement) {
            baseConfigElement = objectElement;
            baseConfig = XferConvert.Deserialize<BaseConfig>(baseConfigElement);
        }

        if (baseConfig is null) {
            throw new Exception($"Error parsing workspace file '{WorkspaceFilePath}'.");
        }

        baseConfig.Workspaces ??= new Dictionary<string, WorkspaceDefinition>();
        // Build mapping ONLY from explicit 'dir' properties. No implicit directory discovery.
        var workspaceDirMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in baseConfig.Workspaces.ToList()) {
            var logicalName = kvp.Key;
            var def = kvp.Value;
            if (string.IsNullOrWhiteSpace(def.Dir)) { continue; }

            var rel = def.Dir.Trim();
            if (rel.StartsWith("./")) { rel = rel[2..]; }
            // Environment variable substitution: <|VAR|> and ${VAR}
            rel = Regex.Replace(rel, @"<\|([A-Z0-9_]+)\|>", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value, RegexOptions.IgnoreCase);
            rel = Regex.Replace(rel, @"\$\{([A-Z0-9_]+)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value, RegexOptions.IgnoreCase);
            var abs = Path.GetFullPath(Path.Combine(ConfigRoot, rel));
            workspaceDirMap[logicalName] = abs;
        }

        // Ensure uniqueness of directory targets (prevent two names pointing to same physical path)
        var pathUniq = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in workspaceDirMap) {
            if (pathUniq.TryGetValue(kvp.Value, out var existingName)) {
                throw new Exception($"Multiple workspace names ('{existingName}', '{kvp.Key}') reference the same directory '{kvp.Value}'. Only one mapping may target a given folder.");
            }
            pathUniq[kvp.Value] = kvp.Key;
        }

        // Load external workspace.xfer for each mapped directory; external file overrides inline
        foreach (var (logicalName, dir) in workspaceDirMap) {
            var wsFile = Path.Combine(dir, "workspace.xfer");
            if (!File.Exists(wsFile)) { continue; }
            try {
                var wsText = File.ReadAllText(wsFile, Encoding.UTF8);
                var wsDoc = XferParser.Parse(wsText);
                if (wsDoc?.Root is ObjectElement wsObj) {
                    var externalDef = XferConvert.Deserialize<WorkspaceDefinition>(wsObj);
                    if (externalDef is not null) {
                        externalDef.Name = logicalName;
                        if (baseConfig.Workspaces.TryGetValue(logicalName, out var inlineDef)) {
                            externalDef.Merge(inlineDef);
                        }
                        baseConfig.Workspaces[logicalName] = externalDef;
                    }
                }
            }
            catch (Exception ex) {
                throw new Exception($"Error parsing workspace file '{wsFile}': {ex.Message}", ex);
            }
        }

        if (baseConfig.ActiveWorkspace is null) {
            // baseConfig.activeWorkspace = "default";
        }

        foreach (var workspaceKvp in baseConfig.Workspaces) {
            var workspace = workspaceKvp.Value;
            if (workspace is null) { continue; }

            workspace.Name = workspaceKvp.Key;

            foreach (var reqKvp in workspace.Requests) {
                reqKvp.Value.Name = reqKvp.Key;
            }

            if (!string.IsNullOrWhiteSpace(workspace.Extend)) {
                if (baseConfig.Workspaces.TryGetValue(workspace.Extend, out var parentWorkspace)) {
                    workspace.Merge(parentWorkspace);
                }
                else {
                    try {
                        _diags.Emit(nameof(IWorkspaceService), new {
                            Message = $"Workspace '{workspace.Name}' extends missing workspace '{workspace.Extend}'. Inheritance skipped.",
                            Workspace = workspace.Name,
                            MissingParent = workspace.Extend
                        });
                    } catch { }
                }
            }
        }

        if (timingsEnabled && sw is not null) {
            var line = $"A2C_TIMINGS: configParse={sw.Elapsed.TotalMilliseconds:F1} ms";
            var mirror = string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("A2C_TIMINGS_MIRROR"), "1", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(line);

            if (mirror) {
                Console.Error.WriteLine(line);
            }

        }

        return baseConfig;
    }

    public IEnumerable<Assembly> LoadConfiguredAssemblies() {
        var loadedAssemblies = new List<Assembly>();

        var assemblyNames = BaseConfig.Assemblies;
        if (assemblyNames is null) {
            return loadedAssemblies;
        }

        foreach (var name in assemblyNames) {
            var path = Path.IsPathRooted(name)
                ? name
                : Path.Combine(_packageDirectory, name);

            if (File.Exists(path)) {
                try {
                    var assembly = Assembly.LoadFrom(path);
                    loadedAssemblies.Add(assembly);
                }
                catch (Exception ex) {
                    _diags.Emit(
                        nameof(IWorkspaceService),
                        new {
                            Message = $"Failed to load assembly {path}: {ex.Message}",
                            ex
                        }
                    );
                }
            }
            else {
                _diags.Emit(
                    nameof(IWorkspaceService),
                    new {
                        Message = $"Assembly not found: {path}"
                    }
                );
            }
        }

        return loadedAssemblies;
    }

    /// <summary>
    /// Saves the current settings back to the configuration file.
    /// </summary>
    public void SaveConfig() {
        try {
            var xfer = XferConvert.Serialize(BaseConfig, Formatting.Indented | Formatting.Spaced);

            File.WriteAllText(WorkspaceFilePath, xfer, Encoding.UTF8);
        }
        catch (Exception ex) {
            throw new Exception($"Error saving workspace file '{WorkspaceFilePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Re-reads the workspace configuration file and refreshes the active workspace and loaded assemblies.
    /// </summary>
    public void ReloadConfig()
    {
        // Remember the current active workspace name before reloading
        var previousWorkspace = CurrentWorkspaceName;

        // Re-parse the config file
        BaseConfig = LoadWorkspace();
        // Reload any configured assemblies
        LoadConfiguredAssemblies();
        // Re-apply the active workspace, preserving the prior selection when available.
        var target = !string.IsNullOrWhiteSpace(previousWorkspace)
            ? previousWorkspace
            : (BaseConfig.ActiveWorkspace ?? string.Empty);

        // Fallback: if no explicit selection, prefer the first defined workspace if any
        if (string.IsNullOrWhiteSpace(target) && BaseConfig.Workspaces is not null && BaseConfig.Workspaces.Count > 0) {
            target = BaseConfig.Workspaces.Keys.First();
        }

        if (!string.IsNullOrWhiteSpace(target)) {
            SetActiveWorkspace(target);
        }
    }
}
