using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParksComputing.Api2Cli.Api;
using ParksComputing.Api2Cli.Api.Http;
using ParksComputing.Api2Cli.Api.Http.Impl;
using ParksComputing.Api2Cli.Api.Store;
using ParksComputing.Api2Cli.Api.Store.Impl;
using ParksComputing.Api2Cli.Scripting.Api.FileSystem;
using ParksComputing.Api2Cli.Scripting.Api.FileSystem.Impl;
using ParksComputing.Api2Cli.Scripting.Api.Package;
using ParksComputing.Api2Cli.Scripting.Api.Package.Impl;
using ParksComputing.Api2Cli.Scripting.Api.Process;
using ParksComputing.Api2Cli.Scripting.Api.Process.Impl;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Scripting.Services.Impl;

namespace ParksComputing.Api2Cli.Scripting;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliScriptingServices(this IServiceCollection services) {
        services.TryAddSingleton<IApi2CliScriptEngineFactory, Api2CliScriptEngineFactory>();
        services.TryAddSingleton<ClearScriptEngine>();
        services.TryAddSingleton<CSharpScriptEngine>();
        services.TryAddSingleton<IPropertyResolver, PropertyResolver>();
        services.TryAddSingleton<IHttpApi, HttpApi>();
        services.TryAddSingleton<IStoreApi, StoreApi>();
        services.TryAddSingleton<IPackageApi, PackageApi>();
        services.TryAddSingleton<IProcessApi, ProcessApi>();
        services.TryAddSingleton<IFileSystemApi, FileSystemApi>();
        services.TryAddSingleton<A2CApi>();
        return services;
    }
}
