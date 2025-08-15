using Microsoft.Extensions.DependencyInjection;
using ParksComputing.Api2Cli.Orchestration.Services;
using ParksComputing.Api2Cli.Orchestration.Services.Impl;

namespace ParksComputing.Api2Cli.Orchestration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApi2CliOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceScriptingOrchestrator, WorkspaceScriptingOrchestrator>();
        return services;
    }
}
