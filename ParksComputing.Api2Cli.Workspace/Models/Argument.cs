using ParksComputing.Xfer.Lang.Attributes;

namespace ParksComputing.Api2Cli.Workspace.Models;

public class Argument {
    [XferProperty("name")]
    public string? Name { get; set; }
    [XferProperty("type")]
    public string? Type { get; set; }
    [XferProperty("description")]
    public string? Description { get; set; }
    [XferProperty("isRequired")]
    public bool IsRequired { get; set; } = false;
}
