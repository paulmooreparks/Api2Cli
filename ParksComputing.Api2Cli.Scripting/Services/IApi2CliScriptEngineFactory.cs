using ParksComputing.Api2Cli.Scripting.Services;

namespace ParksComputing.Api2Cli.Scripting.Services;

/// <summary>
/// Factory interface for creating API 2 CLI script engines
/// </summary>
public interface IApi2CliScriptEngineFactory
{
    /// <summary>
    /// Gets a script engine instance for the specified engine type
    /// </summary>
    /// <param name="engineType">Type of engine (e.g., "javascript", "csharp")</param>
    /// <returns>Script engine instance</returns>
    IApi2CliScriptEngine GetEngine(string engineType);

    IReadOnlyCollection<string> SupportedKinds { get; }
}
