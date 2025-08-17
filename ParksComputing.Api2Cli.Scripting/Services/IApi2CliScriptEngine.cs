namespace ParksComputing.Api2Cli.Scripting.Services;

/// <summary>
/// Interface for API 2 CLI script engines
/// </summary>
public interface IApi2CliScriptEngine {
    /// <summary>
    /// Gets the script object for dynamic access
    /// </summary>
    public dynamic Script { get; }

    /// <summary>
    /// Initializes the script environment
    /// </summary>
    void InitializeScriptEnvironment();

    /// <summary>
    /// Sets a value in the script environment
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Variable value</param>
    void SetValue(string name, object? value);

    /// <summary>
    /// Executes a script and returns the result as a string
    /// </summary>
    /// <param name="script">Script content</param>
    /// <returns>Script execution result</returns>
    string ExecuteScript(string? script);

    /// <summary>
    /// Evaluates a script and returns the result
    /// </summary>
    /// <param name="script">Script content</param>
    /// <returns>Script evaluation result</returns>
    object? EvaluateScript(string? script);

    /// <summary>
    /// Executes a command and returns the result as a string
    /// </summary>
    /// <param name="script">Command content</param>
    /// <returns>Command execution result</returns>
    string ExecuteCommand(string? script);

    /// <summary>
    /// Invokes a pre-request handler
    /// </summary>
    /// <param name="args">Arguments to pass</param>
    void InvokePreRequest(params object?[] args);

    /// <summary>
    /// Invokes a post-response handler
    /// </summary>
    /// <param name="args">Arguments to pass</param>
    /// <returns>Handler result</returns>
    object? InvokePostResponse(params object?[] args);

    /// <summary>
    /// Invokes a named function or script
    /// </summary>
    /// <param name="script">Function or script name</param>
    /// <param name="args">Arguments to pass</param>
    /// <returns>Invocation result</returns>
    object? Invoke(string script, params object?[] args);

    /// <summary>
    /// Adds a host object to the script environment
    /// </summary>
    /// <param name="itemName">Object name</param>
    /// <param name="target">Object instance</param>
    void AddHostObject(string itemName, object? target);

    /// <summary>
    /// Executes an initialization script
    /// </summary>
    /// <param name="script">Initialization script content</param>
    void ExecuteInitScript(string? script);

    /// <summary>
    /// Executes a keyed initialization script (language-aware)
    /// </summary>
    /// <param name="script">Initialization script keyed by language (e.g., javascript/csharp)</param>
    void ExecuteInitScript(ParksComputing.Xfer.Lang.XferKeyedValue? script);
}
