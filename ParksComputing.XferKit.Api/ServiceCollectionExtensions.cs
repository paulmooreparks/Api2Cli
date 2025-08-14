using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParksComputing.Api2Cli.Api.Http;
using ParksComputing.Api2Cli.Api.Http.Impl;
using ParksComputing.Api2Cli.Api.Package;
using ParksComputing.Api2Cli.Api.Package.Impl;
using ParksComputing.Api2Cli.Api.Process;
using ParksComputing.Api2Cli.Api.Process.Impl;
using ParksComputing.Api2Cli.Api.Store;
using ParksComputing.Api2Cli.Api.Store.Impl;

namespace ParksComputing.Api2Cli.Api;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliApiServices(this IServiceCollection services) {
        services.TryAddSingleton<IHttpApi, HttpApi>();
        services.TryAddSingleton<IStoreApi, StoreApi>();
        services.TryAddSingleton<IPackageApi, PackageApi>();
        services.TryAddSingleton<IProcessApi, ProcessApi>();
        services.TryAddSingleton<Api2CliApi>();
        return services;
    }
}
