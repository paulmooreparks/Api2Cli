using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using ParksComputing.XferKit.DataStore.Services;

namespace ParksComputing.XferKit.Workspace.Services.Impl;

internal class SqliteStoreService : IStoreService {
    private readonly IKeyValueStore _store;

    public SqliteStoreService(IKeyValueStore store) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public object this[string key] {
        get => _store[key] ?? throw new InvalidOperationException($"Value for key '{key}' is null");
        set => _store[key] = value;
    }

    public ICollection<string> Keys => _store.Keys;
    public ICollection<object> Values => _store.Values.Where(v => v != null).Cast<object>().ToList();
    public int Count => _store.Count;
    public bool IsReadOnly => _store.IsReadOnly;

    public void Add(string key, object value) => _store.Add(key, value);
    public bool ContainsKey(string key) => _store.ContainsKey(key);
    public bool Remove(string key) => _store.Remove(key);
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object value) {
        if (_store.TryGetValue(key, out var storeValue) && storeValue != null) {
            value = storeValue;
            return true;
        }
        value = default!;
        return false;
    }
    public void Add(KeyValuePair<string, object> item) => _store.Add(item.Key, item.Value);
    public void Clear() => _store.Clear();
    public bool Contains(KeyValuePair<string, object> item) => _store.TryGetValue(item.Key, out var value) && Equals(value, item.Value);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
        var nonNullPairs = _store.Where(kvp => kvp.Value != null).Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value!)).ToArray();
        nonNullPairs.CopyTo(array, arrayIndex);
    }
    public bool Remove(KeyValuePair<string, object> item) => _store.TryGetValue(item.Key, out var value) && Equals(value, item.Value) && _store.Remove(item.Key);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _store.Where(kvp => kvp.Value != null).Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value!)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void ClearStore() => _store.Clear();
    public void Delete(string key) => _store.Remove(key);
    public object? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, object value) => _store[key] = value;
}
