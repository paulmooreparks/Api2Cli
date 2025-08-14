using System;
using System.Collections.Generic;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Scripting.Services.Impl;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl;

internal class XferScriptEngineFactory : IXferScriptEngineFactory
{
    private readonly IDictionary<string, IXferScriptEngine> _engines;

    public XferScriptEngineFactory(ClearScriptEngine jsEngine, CSharpScriptEngine csharpEngine)
    {
        _engines = new Dictionary<string, IXferScriptEngine>(StringComparer.OrdinalIgnoreCase)
        {
            { "javascript", jsEngine },
            { "csharp", csharpEngine }
        };
    }

    public IXferScriptEngine GetEngine(string kind)
    {
        if (_engines.TryGetValue(kind, out var engine)) {
            return engine;
        }
        throw new ArgumentException($"Unknown script engine kind: {kind}", nameof(kind));
    }

    public IReadOnlyCollection<string> SupportedKinds => (IReadOnlyCollection<string>)_engines.Keys;
}
