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

        // Register HttpService with cookie support; ICookieJar depends on store services
        services.TryAddSingleton<ICookieJar, CookieJar>();

        if (!services.Any(s => s.ServiceType == typeof(IHttpService))) {
            services.AddHttpClient<Services.Impl.HttpService>();
            services.AddSingleton<IHttpService>(sp => new Services.Impl.HttpService(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<IAppDiagnostics<IHttpService>>(),
                sp.GetRequiredService<ICookieJar>()));
        }

        services.AddSingleton<IAppDiagnostics<IHttpService>, AppDiagnostics<IHttpService>>();

        return services;
    }

}
