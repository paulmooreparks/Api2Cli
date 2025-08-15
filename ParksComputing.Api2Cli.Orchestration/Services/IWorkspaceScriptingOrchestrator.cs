namespace ParksComputing.Api2Cli.Orchestration.Services;

public interface IWorkspaceScriptingOrchestrator
{
    // Initialize both JS and C# engines, expose a2c, and register scripts from the current BaseConfig
    void Initialize();

    // Optional warmup limited by count
    void Warmup(int limit = 25, bool enable = false, bool debug = false);
}
