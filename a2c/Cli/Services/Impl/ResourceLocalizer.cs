namespace ParksComputing.Api2Cli.Cli.Services.Impl;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

using ParksComputing.Api2Cli.Cli.Services;

internal sealed class ResourceLocalizer : ILocalizer {
    // Base resource names we probe (CLI first, can extend later via ctor args)
    private static readonly string[] _baseNames = [
        "ParksComputing.Api2Cli.Cli.Resources.Messages" // add more base names here if other assemblies supply Messages.resx
    ];

    private readonly List<ResourceManager> _managers = new();
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ResourceLocalizer() {
        foreach (var bn in _baseNames) {
            try {
                _managers.Add(new ResourceManager(bn, Assembly.GetExecutingAssembly()));
            } catch { /* ignore missing */ }
        }
    }

    public string Get(string key, IReadOnlyDictionary<string, object?>? ctx = null, CultureInfo? culture = null) {
        if (string.IsNullOrWhiteSpace(key)) { return string.Empty; }

        // Use simple cache on neutral culture values only (not culture-specific) for speed
        string? value = null;
        var ci = culture ?? CultureInfo.CurrentUICulture;

        foreach (var rm in _managers) {
            try {
                value = rm.GetString(key, ci);
                if (value != null) { break; }
            } catch { /* swallow */ }
        }

        if (value is null) {
            // Fallback to key in brackets so missing localizations are obvious
            value = "[" + key + "]";
        }

        if (ctx is null || ctx.Count == 0) { return value; }

        // Very small token replacement: {name}
        foreach (var kv in ctx) {
            if (kv.Key is null) { continue; }
            var token = "{" + kv.Key + "}";
            value = value.Replace(token, kv.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
        return value;
    }
}
