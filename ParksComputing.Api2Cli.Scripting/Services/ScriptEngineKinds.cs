using System.Collections.Generic;

namespace ParksComputing.Api2Cli.Scripting.Services;

/// <summary>
/// Well-known script engine kinds and their common aliases.
/// Use these constants instead of hard-coded strings.
/// </summary>
public static class ScriptEngineKinds
{
    public const string JavaScript = "javascript";
    public static readonly IReadOnlyCollection<string> JavaScriptAliases = new[] { "javascript", "js" };

    public const string CSharp = "csharp";
    public static readonly IReadOnlyCollection<string> CSharpAliases = new[] { "csharp", "cs" };
}
