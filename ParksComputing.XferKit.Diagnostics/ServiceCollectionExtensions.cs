using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Diagnostics.Services.Impl;

namespace ParksComputing.Api2Cli.Diagnostics;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliDiagnosticsServices(this IServiceCollection services, string name) {
        services.AddSingleton<DiagnosticSource>(new DiagnosticListener(name));
        services.AddSingleton(typeof(IAppDiagnostics<>), typeof(AppDiagnostics<>));
        return services;
    }
}
