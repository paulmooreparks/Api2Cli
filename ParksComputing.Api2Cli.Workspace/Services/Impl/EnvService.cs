using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dotenv.net;

namespace ParksComputing.Api2Cli.Workspace.Services.Impl;

internal class EnvService : IEnvService {
    private Dictionary<string, string> _root = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _active = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Root => _root;
    public IReadOnlyDictionary<string, string> ActiveOverlay => _active;

    public void LoadRoot(string? path) {
        _root = LoadEnvFile(path);
    }

    public void ApplyRoot() {
        ApplyVars(_root);
    }

    public void ApplyOverlay(string? path) {
        RevertOverlay();
        var overlay = LoadEnvFile(path);
        if (overlay.Count == 0) { return; }
        ApplyVars(overlay);
        _active = overlay;
    }

    public void RevertOverlay() {
        if (_active.Count == 0) { return; }
        foreach (var key in _active.Keys) {
            if (_root.TryGetValue(key, out var rootVal)) {
                Environment.SetEnvironmentVariable(key, rootVal);
            }
            else {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
        _active = new(StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyVars(Dictionary<string, string> vars) {
        foreach (var (k, v) in vars) {
            Environment.SetEnvironmentVariable(k, v);
        }
    }

    private static Dictionary<string, string> LoadEnvFile(string? path) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return dict;
        }

        try {
            var vars = DotEnv.Read(new DotEnvOptions(envFilePaths: new[] { path }, ignoreExceptions: true, trimValues: false));
            foreach (var kv in vars) { dict[kv.Key] = kv.Value; }
        }
        catch {
            return dict;
        }

        // secondary ${VAR} expansion
        string Expand(string input, int depth = 0) {
            if (depth > 5 || string.IsNullOrEmpty(input)) {
                return input;
            }

            return Regex.Replace(input, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => {
                var name = m.Groups[1].Value;

                if (dict.TryGetValue(name, out var local)) {
                    return local;
                }

                var env = Environment.GetEnvironmentVariable(name);
                return env ?? m.Value;
            });
        }

        foreach (var key in dict.Keys.ToList()) {
            dict[key] = Expand(dict[key]);
        }

        return dict;
    }
}
