namespace ParksComputing.Api2Cli.Cli.Services.Impl;

using ParksComputing.Api2Cli.Diagnostics.Services.Unified;
using System;
using System.Collections.Generic;

internal sealed class ConsoleWriter : IConsoleWriter {
    private readonly IUnifiedDiagnostics? _diag;
    private readonly object _lock = new();

    public ConsoleWriter(IUnifiedDiagnostics? diag) { _diag = diag; }

    private static string Normalize(string message) => message ?? string.Empty;

    public void Write(string message, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null) {
        lock (_lock) {
            Console.Write(Normalize(message));
        }
        if (!string.IsNullOrEmpty(message)) {
            _diag?.Info(category ?? "cli.out", Normalize(message), code: code, ctx: ctx);
        }
    }

    public void WriteLine(string message = "", string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null) {
        lock (_lock) {
            Console.WriteLine(Normalize(message));
        }
        if (!string.IsNullOrEmpty(message)) {
            _diag?.Info(category ?? "cli.out", Normalize(message), code: code, ctx: ctx);
        }
    }

    public void WriteError(string message, string? category = null, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null) {
    lock (_lock) { Console.Error.WriteLine(Normalize(message)); }
    _diag?.Error(category ?? "cli.error", Normalize(message), code: code, ex: ex, ctx: ctx);
    }
}
