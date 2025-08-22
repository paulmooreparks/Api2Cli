using System;
using System.Text;
using ParksComputing.Api2Cli.Workspace.Models;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Xfer.Lang;
using ParksComputing.Xfer.Lang.Configuration;
using ParksComputing.Xfer.Lang.Elements;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

internal abstract class WorkspaceImportCommandBase {
    protected readonly IWorkspaceService WorkspaceService;

    protected WorkspaceImportCommandBase(IWorkspaceService workspaceService) {
        WorkspaceService = workspaceService;
    }

    protected string GetConfigRoot() => System.IO.Path.GetDirectoryName(WorkspaceService.WorkspaceFilePath) ?? Environment.CurrentDirectory;

    protected bool TryResolveTargetDirectory(string dir, bool force, out string targetDir) {
        var configRoot = GetConfigRoot();
        targetDir = System.IO.Path.IsPathRooted(dir) ? dir : System.IO.Path.GetFullPath(System.IO.Path.Combine(configRoot, dir));
        if (System.IO.Directory.Exists(targetDir) && !force) {
            var existingFile = System.IO.Path.Combine(targetDir, "workspace.xfer");
            if (System.IO.File.Exists(existingFile)) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Directory already contains a workspace.xfer: {existingFile}. Use --force to overwrite.");
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
        Console.WriteLine($"Workspace imported to {wsFilePath} with {requestCount} requests.");
        Console.WriteLine();
        Console.WriteLine("Add this line inside the workspaces block of your config.xfer to activate it:");
        Console.WriteLine($"    {logicalName} {{ dir \"{WorkspaceImportHelpers.GetRelativePath(configRoot, targetDir)}\" }}");
        Console.WriteLine();
        Console.WriteLine("Then run: reload");
    }
}
