using System;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Diagnostics.Services.Unified;

public static class DiagnosticsExtensions {
    public static void Info(this IUnifiedDiagnostics? diag, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => diag?.Log(DiagnosticSeverity.Info, category, message, code, ex, ctx);
    public static void Warn(this IUnifiedDiagnostics? diag, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => diag?.Log(DiagnosticSeverity.Warning, category, message, code, ex, ctx);
    public static void Error(this IUnifiedDiagnostics? diag, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => diag?.Log(DiagnosticSeverity.Error, category, message, code, ex, ctx);
    public static void Critical(this IUnifiedDiagnostics? diag, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => diag?.Log(DiagnosticSeverity.Critical, category, message, code, ex, ctx);
}
