using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#nullable enable

namespace ParksComputing.Api2Cli.Api.Store;

public interface IStoreApi {
    object? Get(string key);
    void Set(string key, object value);
    void Delete(string key);
    void Clear();
    string[] Keys { get; }
    // Non-null array instance; elements may be null
    object?[] Values { get; }
}
