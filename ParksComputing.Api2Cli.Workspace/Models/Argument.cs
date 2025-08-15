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
    // Optional default value specified in Xfer config: supports string, number, boolean, or object
    [XferProperty("default")]
    public object? Default { get; set; }
}
