using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Api.Http;
using ParksComputing.XferKit.Api.Http.Impl;
using ParksComputing.XferKit.Api.Store;
using ParksComputing.XferKit.Api.Store.Impl;
using ParksComputing.XferKit.Scripting.Api.FileSystem;
using ParksComputing.XferKit.Scripting.Api.FileSystem.Impl;
using ParksComputing.XferKit.Scripting.Api.Package;
using ParksComputing.XferKit.Scripting.Api.Package.Impl;
using ParksComputing.XferKit.Scripting.Api.Process;
using ParksComputing.XferKit.Scripting.Api.Process.Impl;
using ParksComputing.XferKit.Scripting.Services;
using ParksComputing.XferKit.Scripting.Services.Impl;

namespace ParksComputing.XferKit.Scripting;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddXferKitScriptingServices(this IServiceCollection services) {
        services.TryAddSingleton<IXferScriptEngine, ClearScriptEngine>();
        services.TryAddSingleton<IPropertyResolver, PropertyResolver>();
        services.TryAddSingleton<IHttpApi, HttpApi>();
        services.TryAddSingleton<IStoreApi, StoreApi>();
        services.TryAddSingleton<IPackageApi, PackageApi>();
        services.TryAddSingleton<IProcessApi, ProcessApi>();
        services.TryAddSingleton<IFileSystemApi, FileSystemApi>();
        services.TryAddSingleton<XferKitApi>();
        return services;
    }
}
