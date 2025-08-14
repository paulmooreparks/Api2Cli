using System;
using System.Collections.Generic;

using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Scripting.Services.Impl;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl;

internal class Api2CliScriptEngineFactory : IApi2CliScriptEngineFactory {
    private readonly IDictionary<string, IApi2CliScriptEngine> _engines;

    public Api2CliScriptEngineFactory(ClearScriptEngine jsEngine, CSharpScriptEngine csharpEngine) {
        _engines = new Dictionary<string, IApi2CliScriptEngine>(StringComparer.OrdinalIgnoreCase)
        {
            { "javascript", jsEngine },
            { "csharp", csharpEngine }
        };
    }

    public IApi2CliScriptEngine GetEngine(string kind) {
        if (_engines.TryGetValue(kind, out var engine)) {
            return engine;
        }
        throw new ArgumentException($"Unknown script engine kind: {kind}", nameof(kind));
    }

    public IReadOnlyCollection<string> SupportedKinds => (IReadOnlyCollection<string>) _engines.Keys;
}
