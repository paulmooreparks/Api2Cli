using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParksComputing.Xfer.Lang.Attributes;

namespace ParksComputing.Api2Cli.Workspace.Models;

public class ScriptDefinition {
    [XferProperty("name")]
    public string? Name { get; set; }
    [XferProperty("description")]
    public string? Description { get; set; }
    [XferProperty("initScript")]
    public string? InitScript { get; set; }
    [XferCaptureTag("initScript")]
    public List<string>? InitScriptTags { get; set; }
    [XferProperty("script")]
    public string? Script { get; set; }
    [XferCaptureTag("script")]
    public List<string>? ScriptTags { get; set; }
    [XferProperty("language")]
    public string? ScriptLanguage { get; set; }
    [XferProperty("arguments")]
    public Dictionary<string, Argument> Arguments { get; set; } = [];

    public void Merge(ScriptDefinition parentScript) {
        if (parentScript is null) {
            return;
        }

        Name ??= parentScript.Name;
        Description ??= parentScript.Description;
    InitScript ??= parentScript.InitScript;
    Script ??= parentScript.Script;
    }

    public (string Lang, string Body) ResolveLanguageAndBody()
    {
    var lang = ScriptLanguage ?? ScriptTags?.FirstOrDefault() ?? "javascript";
    return (lang, Script ?? string.Empty);
    }
}
