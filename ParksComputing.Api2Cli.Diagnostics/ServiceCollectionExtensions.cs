using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Diagnostics.Services.Impl;
using ParksComputing.Api2Cli.Diagnostics.Services.Unified;
using ParksComputing.Api2Cli.Diagnostics.Services.Unified.Impl;

namespace ParksComputing.Api2Cli.Diagnostics;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliDiagnosticsServices(this IServiceCollection services, string name) {
        services.AddSingleton<DiagnosticSource>(new DiagnosticListener(name));
        services.AddSingleton(typeof(IAppDiagnostics<>), typeof(AppDiagnostics<>));
        // Allow controlling diagnostic verbosity via env var A2C_DIAG_MIN (trace|debug|info|warn|warning|error|critical|off|none)
    var minVar = Environment.GetEnvironmentVariable("A2C_DIAG_MIN") ?? string.Empty;
    // Default is OFF unless explicitly enabled via A2C_DIAG_MIN.
    DiagnosticSeverity minSeverity = Parse(minVar);

        // If diagnostics are turned off, register a no-op implementation and skip sink.
        if (minVar.Equals("off", StringComparison.OrdinalIgnoreCase) || minVar.Equals("none", StringComparison.OrdinalIgnoreCase)) {
            services.AddSingleton<IUnifiedDiagnostics, NoopUnifiedDiagnostics>();
            return services;
        }

        // Choose output stream; default stderr. A2C_DIAG_STREAM=stdout|stderr
        var streamVar = Environment.GetEnvironmentVariable("A2C_DIAG_STREAM");
        TextWriter writer = Console.Error;
    if (string.Equals(streamVar, "stdout", StringComparison.OrdinalIgnoreCase)) { writer = Console.Out; }
        services.AddSingleton<IDiagnosticsSink>(sp => new XferLangDiagnosticsSink(writer));
        services.AddSingleton<IUnifiedDiagnostics>(sp => new UnifiedDiagnostics(sp.GetRequiredService<IDiagnosticsSink>(), minSeverity));
        return services;
    }

    private static DiagnosticSeverity Parse(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return DiagnosticSeverity.Critical + 1; // sentinel value meaning off
        }
        return value.ToLowerInvariant() switch {
            "trace" => DiagnosticSeverity.Trace,
            "debug" => DiagnosticSeverity.Debug,
            "info" or "information" => DiagnosticSeverity.Info,
            "warn" or "warning" => DiagnosticSeverity.Warning,
            "error" => DiagnosticSeverity.Error,
            "critical" or "fatal" => DiagnosticSeverity.Critical,
            _ => DiagnosticSeverity.Critical + 1 // treat unknown as off to be safe
        };
    }

    private sealed class NoopUnifiedDiagnostics : IUnifiedDiagnostics {
        public bool IsEnabled(DiagnosticSeverity severity, string category) => false;
        public void Write(DiagnosticEvent evt) { }
        public void Log(DiagnosticSeverity severity, string category, string message, string? code = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? ctx = null) { }
        public void Timing(string name, double ms, IReadOnlyDictionary<string, object?>? ctx = null) { }
        public void Debug(string category, string message, IReadOnlyDictionary<string, object?>? ctx = null) { }
    }
}
