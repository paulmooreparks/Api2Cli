namespace ParksComputing.Api2Cli.Orchestration.Services;

public interface IWorkspaceScriptingOrchestrator
{
    // Initialize both JS and C# engines, expose a2c, and register scripts from the current BaseConfig
    void Initialize();

    // Reset all scripting/orchestration state so that a subsequent Initialize()/ActivateWorkspace
    // sequence behaves exactly like a fresh process start (all scriptInit blocks re-run, caches cleared).
    // Intended to be called after workspace configuration reload.
    void ResetForReload();

    // Activate a specific workspace on demand (lazy). This will:
    // - Ensure engines are initialized
    // - Project the workspace into the JS runtime and run its JS init chain (base-first)
    // - Run the workspace's C# init chain when applicable
    // - Build any C# handlers for this workspace lazily
    void ActivateWorkspace(string workspaceName);

    // Optional warmup limited by count
    void Warmup(int limit = 25, bool enable = false, bool debug = false);

    // Execute pre-request chain across languages/scopes; may mutate headers/parameters/payload
    void InvokePreRequest(
        string workspaceName,
        string requestName,
        IDictionary<string, string> headers,
        IList<string> parameters,
        ref string? payload,
        IDictionary<string, string> cookies,
        object?[] extraArgs
    );

    // Execute post-response chain across languages/scopes and return final result (if any)
    object? InvokePostResponse(
        string workspaceName,
        string requestName,
        int statusCode,
        System.Net.Http.Headers.HttpResponseHeaders headers,
        string responseContent,
        object?[] extraArgs
    );

    // Allow clients (CLI, GUI, etc.) to register a request executor used by script engines
    // when exposing workspace.requests.<name>.execute. The runner returns an object? result.
    void RegisterRequestExecutor(Func<string, string, object?[]?, object?> executor);
}
