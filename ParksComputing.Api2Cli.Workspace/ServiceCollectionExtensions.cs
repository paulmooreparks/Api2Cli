using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParksComputing.Api2Cli.Diagnostics;
using ParksComputing.Api2Cli.Diagnostics.Services;
using ParksComputing.Api2Cli.Diagnostics.Services.Impl;

using ParksComputing.Api2Cli.Workspace.Services.Impl;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Workspace;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliWorkspaceServices(this IServiceCollection services) {
        services.TryAddSingleton<ISettingsService, SettingsService>();
        services.TryAddSingleton<IPackageService, PackageService>();
        services.TryAddSingleton<IStoreService, SqliteStoreService>();
        services.TryAddSingleton<IWorkspaceService, WorkspaceService>();
        return services;
    }
}
