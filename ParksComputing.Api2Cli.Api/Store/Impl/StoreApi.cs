using System;
using System.Linq;

#nullable enable

using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Api.Store.Impl;

internal class StoreApi : IStoreApi {
    private readonly IStoreService _storeService; // resolves current workspace dynamically

    public StoreApi(IStoreService storeService) {
        _storeService = storeService ?? throw new ArgumentNullException(nameof(storeService));
    }

    public object? Get(string key) => _storeService.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, object value) => _storeService[key] = value;
    public void Delete(string key) => _storeService.Remove(key);
    public void Clear() => _storeService.Clear();
    public string[] Keys => _storeService.Keys.ToArray();
    // Non-null array instance; elements may be null.
    [System.Diagnostics.CodeAnalysis.DisallowNull]
    public object?[] Values {
        get {
            // Materialize to ensure snapshot semantics; dictionary may mutate.
            var values = _storeService.Values; // ICollection<object?> dynamic per workspace
            var result = new object?[values.Count];
            int i = 0;

            foreach (var v in values) {
                result[i++] = v; // elements may be null
            }

            return result; // never null, elements individually nullable
        }
    }
}

