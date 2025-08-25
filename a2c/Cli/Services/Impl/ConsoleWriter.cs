namespace ParksComputing.Api2Cli.Cli.Services.Impl;

using ParksComputing.Api2Cli.Diagnostics.Services.Unified;
using System;
using System.Collections.Generic;

internal sealed class ConsoleWriter : IConsoleWriter {
    private readonly IUnifiedDiagnostics? _diag;
    private readonly ILocalizer? _localizer;
    private readonly object _lock = new();

    public ConsoleWriter(IUnifiedDiagnostics? diag, ILocalizer? localizer = null) { _diag = diag; _localizer = localizer; }

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

    public void WriteKey(string key, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => Write(_localizer?.Get(key, ctx) ?? key, category, code ?? key, ctx);

    public void WriteLineKey(string key, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => WriteLine(_localizer?.Get(key, ctx) ?? key, category, code ?? key, ctx);

    public void WriteErrorKey(string key, string? category = null, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null)
        => WriteError(_localizer?.Get(key, ctx) ?? key, category, code ?? key, ex, ctx);

    public string Localize(string key, IReadOnlyDictionary<string, object?>? ctx = null)
        => _localizer?.Get(key, ctx) ?? key;
}
