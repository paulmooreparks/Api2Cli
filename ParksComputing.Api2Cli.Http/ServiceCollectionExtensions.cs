using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Diagnostics.Services.Impl;
using ParksComputing.Api2Cli.Http.Services;

namespace ParksComputing.Api2Cli.Http;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliHttpServices(this IServiceCollection services) {
        if (!services.Any(s => s.ServiceType == typeof(IHttpClientFactory))) {
            services.AddHttpClient();
        }

        if (!services.Any(s => s.ServiceType == typeof(IHttpService))) {
            services.AddHttpClient<IHttpService, Services.Impl.HttpService>();
        }

        services.AddSingleton<IAppDiagnostics<IHttpService>, AppDiagnostics<IHttpService>>();

        return services;
    }

}
