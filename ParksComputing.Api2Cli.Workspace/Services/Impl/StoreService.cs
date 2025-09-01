using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using ParksComputing.Api2Cli.DataStore.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Workspace.Services.Impl;

public class SqliteStoreService : IStoreService {
    private readonly IWorkspaceKeyValueStoreProvider _provider;

    public SqliteStoreService(IWorkspaceKeyValueStoreProvider provider) {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    private IKeyValueStore Current => _provider.GetCurrentStore();

    public object this[string key] {
        get => Current[key] ?? throw new InvalidOperationException($"Value for key '{key}' is null");
        set => Current[key] = value;
    }

    public ICollection<string> Keys => Current.Keys;
    public ICollection<object> Values => Current.Values.Where(v => v != null).Cast<object>().ToList();
    public int Count => Current.Count;
    public bool IsReadOnly => Current.IsReadOnly;

    public void Add(string key, object value) => Current.Add(key, value);
    public bool ContainsKey(string key) => Current.ContainsKey(key);
    public bool Remove(string key) => Current.Remove(key);
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object value) {
        if (Current.TryGetValue(key, out var storeValue) && storeValue != null) {
            value = storeValue;
            return true;
        }
        value = default!;
        return false;
    }
    public void Add(KeyValuePair<string, object> item) => Current.Add(item.Key, item.Value);
    public void Clear() => Current.Clear();
    public bool Contains(KeyValuePair<string, object> item) => Current.TryGetValue(item.Key, out var value) && Equals(value, item.Value);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
        var nonNullPairs = Current.Where(kvp => kvp.Value != null).Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value!)).ToArray();
        nonNullPairs.CopyTo(array, arrayIndex);
    }
    public bool Remove(KeyValuePair<string, object> item) => Current.TryGetValue(item.Key, out var value) && Equals(value, item.Value) && Current.Remove(item.Key);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => Current.Where(kvp => kvp.Value != null).Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value!)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void ClearStore() => Current.Clear();
    public void Delete(string key) => Current.Remove(key);
    public object? Get(string key) => Current.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, object value) => Current[key] = value;
}
