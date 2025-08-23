using System;
using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Diagnostics.Services.Unified;

public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticSeverity Severity,
    string Category,
    string Message,
    string? Code,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Context
);
