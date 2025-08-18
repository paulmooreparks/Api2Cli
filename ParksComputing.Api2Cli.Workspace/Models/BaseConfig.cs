using ParksComputing.Xfer.Lang.Attributes;
using ParksComputing.Xfer.Lang;

namespace ParksComputing.Api2Cli.Workspace.Models;

public class BaseConfig
{
    [XferProperty("activeWorkspace")]
    public string? ActiveWorkspace { get; set; }
    // Language-scoped init scripts as a simple map of language -> raw script body
    // e.g. { javascript: <"...">, csharp: <"..."> }
    // Language-scoped init scripts
    // Use XferKeyedValue so XferLang can deserialize keyed bodies like { javascript <"..."> csharp <"..."> }
    [XferProperty("initScript")]
    public XferKeyedValue? InitScript { get; set; }
    [XferProperty("preRequest")]
    public XferKeyedValue? PreRequest { get; set; }
    [XferCaptureTag("preRequest")]
    public List<string>? PreRequestTags { get; set; }
    [XferProperty("postResponse")]
    public XferKeyedValue? PostResponse { get; set; }
    [XferCaptureTag("postResponse")]
    public List<string>? PostResponseTags { get; set; }
    [XferProperty("properties")]
    public Dictionary<string, object> Properties { get; set; } = [];
    [XferProperty("workspaces")]
    public Dictionary<string, WorkspaceDefinition> Workspaces { get; set; } = [];
    [XferProperty("macros")]
    public Dictionary<string, MacroDefinition> Macros { get; set; } = [];
    [XferProperty("scripts")]
    public Dictionary<string, ScriptDefinition> Scripts { get; set; } = [];
    [XferProperty("assemblies")]
    public string[]? Assemblies { get; set; }
}
