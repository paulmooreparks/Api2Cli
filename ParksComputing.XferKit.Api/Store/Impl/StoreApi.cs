using System;
using System.Linq;

using ParksComputing.Api2Cli.DataStore;
using ParksComputing.Api2Cli.DataStore.Services;

namespace ParksComputing.Api2Cli.Api.Store.Impl;

internal class StoreApi : IStoreApi {
    private readonly IKeyValueStore _store;

    public StoreApi(IKeyValueStore store) {
        _store = store;
    }

    public object? get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public void set(string key, object value) => _store[key] = value;

    public void delete(string key) => _store.Remove(key);

    public void clear() => _store.Clear();

    public string[] keys => [.. _store.Keys];

    public object[]? values {
        get {
            return [.. _store.Values];
        }
    }
}
