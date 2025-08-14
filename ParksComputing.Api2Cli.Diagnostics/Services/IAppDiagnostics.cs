using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Diagnostics.Services;

public interface IAppDiagnostics<T> {
    void Emit(string eventName, object? data = null);
    bool IsEnabled(string eventName);
}
