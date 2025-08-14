using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Api.Store;

public interface IStoreApi {
    object? get(string key);
    void set(string key, object value);
    void delete(string key);
    void clear();
    string[] keys { get; }
    object[]? values { get; }
}
