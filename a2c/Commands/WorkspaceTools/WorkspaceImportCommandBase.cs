using System;
using System.Text;
using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Models;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Xfer.Lang;
using ParksComputing.Xfer.Lang.Configuration;
using ParksComputing.Xfer.Lang.Elements;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

internal abstract class WorkspaceImportCommandBase {
    protected readonly IWorkspaceService WorkspaceService;
    protected readonly IConsoleWriter ConsoleWriter;

    protected WorkspaceImportCommandBase(IWorkspaceService workspaceService, IConsoleWriter consoleWriter) {
        WorkspaceService = workspaceService;
        ConsoleWriter = consoleWriter;
    }

    protected string GetConfigRoot() => System.IO.Path.GetDirectoryName(WorkspaceService.WorkspaceFilePath) ?? Environment.CurrentDirectory;

    protected bool TryResolveTargetDirectory(string dir, bool force, out string targetDir) {
        var configRoot = GetConfigRoot();
        targetDir = System.IO.Path.IsPathRooted(dir) ? dir : System.IO.Path.GetFullPath(System.IO.Path.Combine(configRoot, dir));
        if (System.IO.Directory.Exists(targetDir) && !force) {
            var existingFile = System.IO.Path.Combine(targetDir, "workspace.xfer");
            if (System.IO.File.Exists(existingFile)) {
                ConsoleWriter.WriteErrorKey("import.dir.exists", category: "cli.workspace.import", code: "import.dir.exists", ctx: new Dictionary<string, object?> { ["file"] = existingFile });
                return false;
            }
        }
        else if (!System.IO.Directory.Exists(targetDir)) {
            System.IO.Directory.CreateDirectory(targetDir);
        }
        return true;
    }

    protected string SerializeWorkspace(WorkspaceDefinition wsDef) {
        var document = new XferDocument(new ObjectElement());
        if (document.Root is not ObjectElement root) {
            throw new InvalidOperationException("Failed to initialize Xfer document root object");
        }

        // description (always present)
        root.Add(new KeyValuePairElement(new KeywordElement("description"), new StringElement(wsDef.Description ?? string.Empty)));

        // baseUrl (optional)
        if (!string.IsNullOrWhiteSpace(wsDef.BaseUrl)) {
            root.Add(new KeyValuePairElement(new KeywordElement("baseUrl"), new StringElement(wsDef.BaseUrl)));
        }

        // requests block
        var requestsObject = new ObjectElement();
        foreach (var req in wsDef.Requests.Values.OrderBy(r => r.Name)) {
            if (string.IsNullOrWhiteSpace(req.Name)) {
                continue;
            }

            var reqObj = new ObjectElement();
            reqObj.Add(new KeyValuePairElement(new KeywordElement("endpoint"), new StringElement(req.Endpoint ?? "/")));
            reqObj.Add(new KeyValuePairElement(new KeywordElement("method"), new StringElement((req.Method ?? "GET").ToUpperInvariant())));

            requestsObject.Add(new KeyValuePairElement(new KeywordElement(req.Name!), reqObj));
        }
        root.Add(new KeyValuePairElement(new KeywordElement("requests"), requestsObject));

        return document.ToXfer(Formatting.Indented | Formatting.Spaced, ' ', 4, 0);
    }

    protected void WriteWorkspaceFile(string targetDir, string serialized) {
        var wsFilePath = System.IO.Path.Combine(targetDir, "workspace.xfer");
        System.IO.File.WriteAllText(wsFilePath, serialized);
    }

    protected void EmitActivationGuidance(string logicalName, string targetDir, int requestCount) {
        var configRoot = GetConfigRoot();
        var wsFilePath = System.IO.Path.Combine(targetDir, "workspace.xfer");
    ConsoleWriter.WriteLineKey("import.success", category: "cli.workspace.import", code: "import.success", ctx: new Dictionary<string, object?> { ["file"] = wsFilePath, ["count"] = requestCount });
    ConsoleWriter.WriteLineKey("import.blank", category: "cli.workspace.import", code: "import.blank");
    ConsoleWriter.WriteLineKey("import.guidance.header", category: "cli.workspace.import", code: "import.guidance.header");
    var rel = WorkspaceImportHelpers.GetRelativePath(configRoot, targetDir);
    ConsoleWriter.WriteLineKey("import.guidance.line", category: "cli.workspace.import", code: "import.guidance.line", ctx: new Dictionary<string, object?> { ["line"] = $"    {logicalName} {{ dir \"{rel}\" }}" });
    ConsoleWriter.WriteLineKey("import.blank2", category: "cli.workspace.import", code: "import.blank2");
    ConsoleWriter.WriteLineKey("import.guidance.reload", category: "cli.workspace.import", code: "import.guidance.reload");
    }
}
