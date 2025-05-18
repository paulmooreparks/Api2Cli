using ParksComputing.Xfer.Lang.Attributes;

namespace ParksComputing.XferKit.Workspace.Models;

public class FolderDefinition {
    [XferProperty("name")]
    public string? Name { get; set; }
    [XferProperty("description")]
    public string? Description { get; set; }
    [XferProperty("baseUrl")]
    public string? BaseUrl { get; set; }
    [XferProperty("initScript")]
    public string? InitScript { get; set; }
    [XferProperty("preRequest")]
    public string? PreRequest { get; set; }
    [XferProperty("postResponse")]
    public string? PostResponse { get; set; }
    [XferProperty("properties")]
    public Dictionary<string, string> Properties { get; set; } = [];
    [XferProperty("folders")]
    public Dictionary<string, FolderDefinition> Folders { get; set; } = [];
    [XferProperty("requests")]
    public Dictionary<string, RequestDefinition> Requests { get; set; } = [];
    [XferProperty("scripts")]
    public Dictionary<string, ScriptDefinition> Scripts { get; set; } = [];
    [XferProperty("macros")]
    public Dictionary<string, MacroDefinition> Macros { get; set; } = [];
}
