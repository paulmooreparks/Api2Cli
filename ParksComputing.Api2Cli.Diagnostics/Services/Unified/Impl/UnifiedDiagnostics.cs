using System;
using System.Collections.Generic;
using ParksComputing.Api2Cli.Diagnostics.Services.Unified;

namespace ParksComputing.Api2Cli.Diagnostics.Services.Unified.Impl;

internal sealed class UnifiedDiagnostics : IUnifiedDiagnostics {
    private readonly IDiagnosticsSink _sink; private readonly DiagnosticSeverity _min;
    public UnifiedDiagnostics(IDiagnosticsSink sink, DiagnosticSeverity min = DiagnosticSeverity.Info) { _sink = sink; _min = min; }
    public bool IsEnabled(DiagnosticSeverity severity, string category) => severity >= _min;
    public void Write(DiagnosticEvent evt) {
        if (IsEnabled(evt.Severity, evt.Category)) {
            _sink.Publish(evt);
        }
    }
    public void Log(DiagnosticSeverity severity, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null) {
        Write(new DiagnosticEvent(DateTimeOffset.UtcNow, severity, category, message, code, ex, ctx ?? Empty.Instance)); }
    public void Timing(string name, double ms, IReadOnlyDictionary<string, object?>? ctx = null) {
        var dict = new Dictionary<string, object?> { { "metric", name }, { "ms", ms } };
        if (ctx != null) {
            foreach (var kv in ctx) {
                dict[kv.Key] = kv.Value;
            }
        }
        Log(DiagnosticSeverity.Debug, "Timing", name, null, null, dict);
    }
    public void Debug(string category, string message, IReadOnlyDictionary<string, object?>? ctx = null) => Log(DiagnosticSeverity.Debug, category, message, null, null, ctx);
    private sealed class Empty : IReadOnlyDictionary<string, object?> { public static readonly Empty Instance = new(); public int Count=>0; public IEnumerable<string> Keys=>Array.Empty<string>(); public IEnumerable<object?> Values=>Array.Empty<object?>(); public object? this[string key]=>null; public bool ContainsKey(string key)=>false; public bool TryGetValue(string key, out object? value){ value=null; return false;} public IEnumerator<KeyValuePair<string, object?>> GetEnumerator(){ yield break;} System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator(); }
}
