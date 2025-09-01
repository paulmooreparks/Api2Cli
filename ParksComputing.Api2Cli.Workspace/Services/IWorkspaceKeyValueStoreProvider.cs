using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

using ParksComputing.Api2Cli.DataStore.Services;

namespace ParksComputing.Api2Cli.Workspace.Services;

public interface IWorkspaceKeyValueStoreProvider {
    IKeyValueStore GetCurrentStore();
    IKeyValueStore GetStoreFor(string workspaceName);
}

public sealed class WorkspaceKeyValueStoreProvider : IWorkspaceKeyValueStoreProvider {
    private readonly IWorkspaceService _workspaceService;
    private readonly string _defaultDatabasePath;
    private readonly ConcurrentDictionary<string, IKeyValueStore> _stores = new();

    public WorkspaceKeyValueStoreProvider(IWorkspaceService workspaceService, string defaultDatabasePath) {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _defaultDatabasePath = defaultDatabasePath ?? throw new ArgumentNullException(nameof(defaultDatabasePath));

        _stores.GetOrAdd(string.Empty, _ => CreateStoreFor(string.Empty));
        if (!string.IsNullOrEmpty(_workspaceService.CurrentWorkspaceName)) {
            _stores.GetOrAdd(_workspaceService.CurrentWorkspaceName, n => CreateStoreFor(n));
        }
        _workspaceService.ActiveWorkspaceChanged += name => {
            _stores.GetOrAdd(name ?? string.Empty, n => CreateStoreFor(n ?? string.Empty));
        };
    }

    public IKeyValueStore GetCurrentStore() => GetStoreFor(_workspaceService.CurrentWorkspaceName);

    public IKeyValueStore GetStoreFor(string workspaceName) {
        workspaceName ??= string.Empty;
        return _stores.GetOrAdd(workspaceName, n => CreateStoreFor(n));
    }

    private IKeyValueStore CreateStoreFor(string workspaceName) {
        string dbPath;
        if (string.IsNullOrEmpty(workspaceName)) {
            // Global (no active workspace) uses the default root database path
            dbPath = _defaultDatabasePath;
        }
        else {
            // Attempt to resolve the physical directory mapped to this workspace (can be anywhere on disk)
            var resolvedDir = ResolveWorkspaceDirectory(workspaceName);
            if (resolvedDir is not null) {
                try { Directory.CreateDirectory(resolvedDir); }
                catch (Exception ex) {
                    // Failed to create target directory; fallback will proceed with resolvedDir despite issue.
                    System.Diagnostics.Debug.WriteLine($"[WorkspaceStore] CreateDirectory failed for '{resolvedDir}': {ex.Message}");
                }
                dbPath = Path.Combine(resolvedDir, "store.sqlite");
            }
            else {
                // Fallback to legacy under the .a2c root/workspaces/<name>
                var baseDir = Path.GetDirectoryName(_defaultDatabasePath)!;
                var safeName = Sanitize(workspaceName);
                var wsDir = Path.Combine(baseDir, "workspaces", safeName);
                Directory.CreateDirectory(wsDir);
                dbPath = Path.Combine(wsDir, "store.sqlite");
            }
        }
        return new ParksComputing.Api2Cli.DataStore.Services.Impl.SqliteKeyValueStore(dbPath);
    }

    private string? ResolveWorkspaceDirectory(string workspaceName) {
        try {
            var cfg = _workspaceService.BaseConfig;
            if (cfg?.Workspaces is null) { return null; }
            if (!cfg.Workspaces.TryGetValue(workspaceName, out var def) || def is null) { return null; }
            if (string.IsNullOrWhiteSpace(def.Dir)) { return null; }

            var raw = def.Dir.Trim();
            if (raw.StartsWith("./")) { raw = raw[2..]; }
            // Environment variable substitution patterns reused from WorkspaceService
            raw = Regex.Replace(raw, @"<\|([A-Z0-9_]+)\|>", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value, RegexOptions.IgnoreCase);
            raw = Regex.Replace(raw, @"\$\{([A-Z0-9_]+)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value, RegexOptions.IgnoreCase);

            string abs;
            if (Path.IsPathRooted(raw)) {
                abs = Path.GetFullPath(raw);
            }
            else {
                var configRoot = Path.GetDirectoryName(_defaultDatabasePath)!; // points at .a2c root
                abs = Path.GetFullPath(Path.Combine(configRoot, raw));
            }

            return abs;
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceStore] ResolveWorkspaceDirectory failed for '{workspaceName}': {ex.Message}");
            return null; // degrade gracefully; caller will fallback
        }
    }

    private static string Sanitize(string name) {
        foreach (var c in Path.GetInvalidFileNameChars()) {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }
}
