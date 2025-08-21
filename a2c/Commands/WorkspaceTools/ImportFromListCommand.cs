using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;

using Cliffer;

using ParksComputing.Api2Cli.Workspace;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("import-list", "Create a workspace from an API description list (METHOD path per line)", Parent = "workspace")]
[Option(typeof(string), "--name", "Name for the new workspace", ["-n"], IsRequired = true)]
[Option(typeof(string), "--spec", "Path or URL to a text file with lines like: GET /pets", ["-s"], IsRequired = true)]
[Option(typeof(string), "--baseurl", "Base URL to set for the workspace (e.g., https://api.example.com)", ["-b"], IsRequired = false)]
[Option(typeof(bool), "--force", "Overwrite existing workspace if it already exists", ["-f"], IsRequired = false)]
internal class ImportFromListCommand(
    IWorkspaceService workspaceService
) {
    public async Task<int> Execute(
        [OptionParam("--name")] string name,
        [OptionParam("--spec")] string spec,
        [OptionParam("--baseurl")] string? baseurl,
        [OptionParam("--force")] bool force
    ) {
        try {
            var ws = workspaceService;
            if (!force && ws.BaseConfig.Workspaces.ContainsKey(name)) {
                Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Workspace '{name}' already exists. Use --force to overwrite.");
                return Result.InvalidArguments;
            }

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
                var reqName = MakeRequestName(method, path, null);
                var reqDef = new ParksComputing.Api2Cli.Workspace.Models.RequestDefinition {
                    Name = reqName,
                    Description = $"{method} {path}",
                    Endpoint = path,
                    Method = method,
                };
                wsDef.Requests[reqName] = reqDef;
            }

            ws.BaseConfig.Workspaces[name] = wsDef;
            ws.SaveConfig();
            Console.WriteLine($"Workspace '{name}' created with {wsDef.Requests.Count} requests. Run 'reload' to activate it.");
            return Result.Success;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Import failed: {ex.Message}");
            return Result.Error;
        }
    }

    private static string MakeRequestName(string method, string path, string? operationId) {
        if (!string.IsNullOrWhiteSpace(operationId)) {
            return operationId;
        }
        var p = (path ?? string.Empty).Trim();
        if (p.StartsWith("/")) { p = p.Substring(1); }
        var chars = p.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var baseName = new string(chars);
        if (string.IsNullOrWhiteSpace(baseName)) {
            baseName = "root";
        }
        return $"{method.ToLowerInvariant()}_{baseName}";
    }
}
