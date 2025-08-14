using Microsoft.Extensions.DependencyInjection;

using ParksComputing.Api2Cli.DataStore.Services;
using ParksComputing.Api2Cli.DataStore.Services.Impl;
namespace ParksComputing.Api2Cli.DataStore;

public static class DataStoreServiceCollectionExtensions {
    public static IServiceCollection AddApi2CliDataStore(this IServiceCollection services, string databasePath) {
        services.AddSingleton<IKeyValueStore>(_ => new SqliteKeyValueStore(databasePath));
        return services;
    }
}