namespace ParksComputing.Api2Cli.Cli.Services;

using ParksComputing.Api2Cli.Diagnostics.Services.Unified;
using System;
using System.Collections.Generic;

// Thin abstraction so commands can emit primary user messages while also mirroring to diagnostics.
// Intentionally minimal to keep i18n straightforward (messages passed in are raw keys or formatted text).
public interface IConsoleWriter {
    void Write(string message, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null);
    void WriteLine(string message = "", string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null);
    void WriteError(string message, string? category = null, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null);

    // Localization-aware helpers (key resolves to localized string). Category & code unchanged.
    void WriteKey(string key, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null);
    void WriteLineKey(string key, string? category = null, string? code = null, IReadOnlyDictionary<string, object?>? ctx = null);
    void WriteErrorKey(string key, string? category = null, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null);
    /// <summary>
    /// Returns a localized string for the given key without writing it to the console (for interactive prompts etc.).
    /// Falls back to the key when no localization is available.
    /// </summary>
    string Localize(string key, IReadOnlyDictionary<string, object?>? ctx = null);
}
