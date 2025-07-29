using ParksComputing.XferKit.Scripting.Services;

namespace ParksComputing.XferKit.Scripting.Services;

public interface IXferScriptEngineFactory {
    IXferScriptEngine GetEngine(string kind);
    IReadOnlyCollection<string> SupportedKinds { get; }
}
