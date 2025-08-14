using ParksComputing.Api2Cli.Scripting.Services;

namespace ParksComputing.Api2Cli.Scripting.Services;

public interface IApi2CliScriptEngineFactory {
    IApi2CliScriptEngine GetEngine(string kind);
    IReadOnlyCollection<string> SupportedKinds { get; }
}
