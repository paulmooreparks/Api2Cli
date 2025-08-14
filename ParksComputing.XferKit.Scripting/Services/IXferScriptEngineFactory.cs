using ParksComputing.Api2Cli.Scripting.Services;

namespace ParksComputing.Api2Cli.Scripting.Services;

public interface IXferScriptEngineFactory {
    IXferScriptEngine GetEngine(string kind);
    IReadOnlyCollection<string> SupportedKinds { get; }
}
