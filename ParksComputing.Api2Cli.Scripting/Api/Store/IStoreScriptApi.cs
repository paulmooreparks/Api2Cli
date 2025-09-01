using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Scripting.Api.Store;

// Distinct scripting interface name to avoid collision with ParksComputing.Api2Cli.Api.Store.IStoreApi
public interface IStoreScriptApi {
    [ScriptMember("get")] object? Get(string key);
    [ScriptMember("set")] void Set(string key, object value);
    [ScriptMember("delete")] void Delete(string key);
    [ScriptMember("clear")] void Clear();
    [ScriptMember("keys")] string[] Keys { get; }
    [ScriptMember("values")] object?[] Values { get; }
}
