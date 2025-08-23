using System;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Diagnostics.Services.Unified;

public interface IUnifiedDiagnostics {
    bool IsEnabled(DiagnosticSeverity severity, string category);
    void Write(DiagnosticEvent evt);
    void Log(DiagnosticSeverity severity, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null);
    void Timing(string name, double ms, IReadOnlyDictionary<string, object?>? ctx = null);
    void Debug(string category, string message, IReadOnlyDictionary<string, object?>? ctx = null);
}
