using System;
using System.Linq;

#nullable enable

using ParksComputing.Api2Cli.DataStore;
using ParksComputing.Api2Cli.DataStore.Services;

namespace ParksComputing.Api2Cli.Api.Store.Impl;

internal class StoreApi : IStoreApi {
    private readonly IKeyValueStore _store;

    public StoreApi(IKeyValueStore store) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public object? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, object value) => _store[key] = value;

    public void Delete(string key) => _store.Remove(key);

    public void Clear() => _store.Clear();

    public string[] Keys => _store.Keys.ToArray();

    // Return non-null array; elements may be null.
    [System.Diagnostics.CodeAnalysis.DisallowNull]
    public object?[] Values {
        get {
            var values = _store.Values; // ICollection<object?>
            var result = new object?[values.Count];
            int i = 0;
            foreach (var v in values) {
                result[i++] = v;
            }
            return result;
        }
    }
}

