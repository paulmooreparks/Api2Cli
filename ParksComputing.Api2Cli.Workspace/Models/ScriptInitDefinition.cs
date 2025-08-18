using ParksComputing.Xfer.Lang.Attributes;

namespace ParksComputing.Api2Cli.Workspace.Models;

/// <summary>
/// Global, language-scoped script initialization container.
/// Keys must match engine names (e.g., "javascript", "csharp").
/// Values are raw script bodies parsed by XferLang verbatim strings.
/// </summary>
public class ScriptInitDefinition
{
    [XferProperty("javascript")]
    public string? JavaScript { get; set; }

    [XferProperty("csharp")]
    public string? CSharp { get; set; }
}
