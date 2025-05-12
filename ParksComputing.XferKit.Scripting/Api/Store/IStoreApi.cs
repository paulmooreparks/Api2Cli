using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ClearScript;

namespace ParksComputing.XferKit.Api.Store;

public interface IStoreApi {
    [ScriptMember("get")]
    object? Get(string key);
    [ScriptMember("set")]
    void Set(string key, object value);
    [ScriptMember("delete")]
    void Delete(string key);
    [ScriptMember("clear")]
    void Clear();
    [ScriptMember("keys")]
    string[] Keys { get; }
    [ScriptMember("values")]
    object[]? Values { get; }
}
