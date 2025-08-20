using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Scripting.Services.Impl;
using static ParksComputing.Api2Cli.Scripting.Services.ScriptEngineKinds;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl;

/// <summary>
/// Factory implementation for creating API 2 CLI script engines
/// </summary>
internal class Api2CliScriptEngineFactory : IApi2CliScriptEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private static readonly IReadOnlyCollection<string> _supportedKinds = new List<string>(JavaScriptAliases.Concat(CSharpAliases));

    public Api2CliScriptEngineFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the supported engine types
    /// </summary>
    public IReadOnlyCollection<string> SupportedKinds => _supportedKinds;

    /// <summary>
    /// Gets a script engine instance for the specified engine type
    /// </summary>
    /// <param name="engineType">Type of engine (e.g., "javascript", "csharp")</param>
    /// <returns>Script engine instance</returns>
    public IApi2CliScriptEngine GetEngine(string engineType)
    {
        var key = engineType?.ToLowerInvariant();
        var stubCSharp = string.Equals(Environment.GetEnvironmentVariable("A2C_STUB_CSHARP"), "1", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(Environment.GetEnvironmentVariable("A2C_STUB_CSHARP"), "true", StringComparison.OrdinalIgnoreCase);
        return key switch
        {
            var k when k != null && JavaScriptAliases.Contains(k) => _serviceProvider.GetRequiredService<ClearScriptEngine>(),
            var k when k != null && CSharpAliases.Contains(k) => stubCSharp
                ? _serviceProvider.GetRequiredService<NoOpCSharpScriptEngine>()
                : _serviceProvider.GetRequiredService<CSharpScriptEngine>(),
            _ => _serviceProvider.GetRequiredService<ClearScriptEngine>() // Default to JavaScript
        };
    }
}
