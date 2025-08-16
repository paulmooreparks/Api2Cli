using ParksComputing.Xfer.Lang.Attributes;
using ParksComputing.Xfer.Lang;

namespace ParksComputing.Api2Cli.Workspace.Models;

public class FolderDefinition {
    [XferProperty("name")]
    public string? Name { get; set; }
    [XferProperty("description")]
    public string? Description { get; set; }
    [XferProperty("baseUrl")]
    public string? BaseUrl { get; set; }
    [XferProperty("initScript")]
    public XferKeyedValue? InitScript { get; set; }
    [XferProperty("preRequest")]
    public XferKeyedValue? PreRequest { get; set; }
    [XferProperty("postResponse")]
    public XferKeyedValue? PostResponse { get; set; }
    [XferProperty("properties")]
    public Dictionary<string, object> Properties { get; set; } = [];
    [XferProperty("folders")]
    public Dictionary<string, FolderDefinition> Folders { get; set; } = [];
    [XferProperty("requests")]
    public Dictionary<string, RequestDefinition> Requests { get; set; } = [];
    [XferProperty("scripts")]
    public Dictionary<string, ScriptDefinition> Scripts { get; set; } = [];
    [XferProperty("macros")]
    public Dictionary<string, MacroDefinition> Macros { get; set; } = [];
}
