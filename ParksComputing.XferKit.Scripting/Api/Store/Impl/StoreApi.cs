using System;
using System.Linq;

using ParksComputing.XferKit.DataStore;
using ParksComputing.XferKit.DataStore.Services;

namespace ParksComputing.XferKit.Api.Store.Impl;

internal class StoreApi : IStoreApi {
    private readonly IKeyValueStore _store;

    public StoreApi(IKeyValueStore store) {
        _store = store;
    }

    public object? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, object value) => _store[key] = value;

    public void Delete(string key) => _store.Remove(key);

    public void Clear() => _store.Clear();

    public string[] Keys => [.. _store.Keys];

    public object[]? Values {
        get {
            return [.. _store.Values];
        }
    }
}
