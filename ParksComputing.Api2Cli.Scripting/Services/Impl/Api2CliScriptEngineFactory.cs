using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Scripting.Services.Impl;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl;

/// <summary>
/// Factory implementation for creating API 2 CLI script engines
/// </summary>
internal class Api2CliScriptEngineFactory : IApi2CliScriptEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private static readonly IReadOnlyCollection<string> _supportedKinds = new[]
    {
        "javascript", "js", "csharp", "cs"
    };

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
        return engineType?.ToLowerInvariant() switch
        {
            "javascript" or "js" => _serviceProvider.GetRequiredService<ClearScriptEngine>(),
            "csharp" or "cs" => _serviceProvider.GetRequiredService<CSharpScriptEngine>(),
            _ => _serviceProvider.GetRequiredService<ClearScriptEngine>() // Default to JavaScript
        };
    }
}
