using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("import-list", "Import a simple METHOD path list into a new workspace folder (does NOT modify config.xfer)", Parent = "workspace")]
[Option(typeof(string), "--name", "Logical workspace name (used in guidance output)", ["-n"], IsRequired = true)]
[Option(typeof(string), "--dir", "Target workspace directory to create (relative or absolute)", ["-d"], IsRequired = false)]
[Option(typeof(string), "--spec", "Path or URL to a text file with lines like: GET /pets", ["-s"], IsRequired = true)]
[Option(typeof(string), "--baseurl", "Base URL to set (e.g., https://api.example.com)", ["-b"], IsRequired = false)]
[Option(typeof(bool), "--force", "Overwrite existing directory and workspace.xfer", ["-f"], IsRequired = false)]
internal class ImportFromListCommand(
    IWorkspaceService workspaceService
) : WorkspaceImportCommandBase(workspaceService) {
    public async Task<int> Execute(
        [OptionParam("--name")] string name,
    [OptionParam("--dir")] string? dir,
        [OptionParam("--spec")] string spec,
        [OptionParam("--baseurl")] string? baseurl,
        [OptionParam("--force")] bool force
    )
    {
        try {
            dir ??= name; // default directory name from logical name if not supplied
            if (!TryResolveTargetDirectory(dir, force, out var targetDir)) { return Result.InvalidArguments; }

            string content;
            if (spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                using var http = new HttpClient();
                content = await http.GetStringAsync(spec);
            }
            else {
                content = File.ReadAllText(spec);
            }

            var wsDef = new ParksComputing.Api2Cli.Workspace.Models.WorkspaceDefinition {
                Name = name,
                Description = $"Imported from {spec}",
                BaseUrl = baseurl ?? string.Empty
            };

            var lines = content.Replace("\r\n", "\n").Split('\n');

            foreach (var raw in lines) {
                var line = (raw ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#")) { continue; }
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2) { continue; }
                var method = parts[0].ToUpperInvariant();
                var path = parts[1];
                var reqName = WorkspaceImportHelpers.MakeRequestName(method, path, null);
                var reqDef = new ParksComputing.Api2Cli.Workspace.Models.RequestDefinition {
                    Name = reqName,
                    Description = $"{method} {path}",
                    Endpoint = path,
                    Method = method,
                };
                wsDef.Requests[reqName] = reqDef;
            }

            var serialized = SerializeWorkspace(wsDef);
            WriteWorkspaceFile(targetDir, serialized);
            EmitActivationGuidance(name, targetDir, wsDef.Requests.Count);
            return Result.Success;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Import failed: {ex.Message}");
            return Result.Error;
        }
    }

    // Helpers moved to base / WorkspaceImportHelpers
}
