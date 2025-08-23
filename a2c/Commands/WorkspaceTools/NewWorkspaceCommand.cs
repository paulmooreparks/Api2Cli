using System.CommandLine;
using Cliffer;
using ParksComputing.Api2Cli.Workspace.Services;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Cli.Commands.WorkspaceTools;

[Command("new", "Create an empty workspace scaffold", Parent = "workspace")]
[Option(typeof(string), "--name", "Display name (used to derive slug if --slug not provided)", new[] {"-n"}, IsRequired = true)]
[Option(typeof(string), "--slug", "Explicit folder slug (lowercase, ascii, dash-separated)", IsRequired = false)]
[Option(typeof(string), "--baseurl", "Optional base URL", new[] {"-b"}, IsRequired = false)]
[Option(typeof(bool), "--force", "Overwrite existing workspace folder", new[] {"-f"}, IsRequired = false)]
internal class NewWorkspaceCommand(
    IWorkspaceService workspaceService,
    IConsoleWriter consoleWriter
) {
    private readonly IWorkspaceService _ws = workspaceService;
    private readonly IConsoleWriter _console = consoleWriter;

    public int Execute(
        [OptionParam("--name")] string name,
        [OptionParam("--slug")] string? slug,
        [OptionParam("--baseurl")] string? baseUrl,
        [OptionParam("--force")] bool force
    ) {
        try {
            slug ??= SlugUtil.ToSlug(name);
            if (string.IsNullOrWhiteSpace(slug)) {
                _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Derived slug is empty.", category: "cli.workspace.new", code: "slug.empty");
                return Result.InvalidArguments;
            }
            if (slug.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c=='-'))) {
                _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Invalid slug '{slug}'. Use lowercase letters, digits, and dashes only.", category: "cli.workspace.new", code: "slug.invalid", ctx: new Dictionary<string, object?> { ["slug"] = slug });
                return Result.InvalidArguments;
            }

            if (!force && _ws.BaseConfig.Workspaces.ContainsKey(slug)) {
                _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Workspace '{slug}' already exists. Use --force to overwrite.", category: "cli.workspace.new", code: "workspace.exists", ctx: new Dictionary<string, object?> { ["slug"] = slug });
                return Result.InvalidArguments;
            }

            var configRoot = System.IO.Path.GetDirectoryName(_ws.WorkspaceFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var wsDir = System.IO.Path.Combine(configRoot, "workspaces", slug);
            if (System.IO.Directory.Exists(wsDir)) {
                if (!force) {
                    _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Directory already exists: {wsDir}. Use --force to overwrite.", category: "cli.workspace.new", code: "directory.exists", ctx: new Dictionary<string, object?> { ["dir"] = wsDir });
                    return Result.InvalidArguments;
                }
            } else {
                System.IO.Directory.CreateDirectory(wsDir);
            }

            var wsFile = System.IO.Path.Combine(wsDir, "workspace.xfer");
            if (System.IO.File.Exists(wsFile) && !force) {
                _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} workspace.xfer already exists for '{slug}'. Use --force to overwrite.", category: "cli.workspace.new", code: "workspace.file.exists", ctx: new Dictionary<string, object?> { ["slug"] = slug, ["file"] = wsFile });
                return Result.InvalidArguments;
            }

            var def = new WorkspaceDefinition {
                Name = slug,
                Description = name,
                BaseUrl = baseUrl ?? string.Empty
            };

            _ws.BaseConfig.Workspaces[slug] = def;
            _ws.SaveConfig();

            var escapedName = name.Replace("\"", "\\\"");
            var escapedBase = (def.BaseUrl ?? string.Empty).Replace("\"", "\\\"");
            var baseLine = string.IsNullOrWhiteSpace(def.BaseUrl) ? string.Empty : $"\nbaseUrl \"{escapedBase}\"";
            var workspaceFileContent = $"{{\ndescription \"{escapedName}\"{baseLine}\nrequests {{ }}\nscripts {{ }}\nmacros {{ }}\nproperties {{ }}\n}}";
            System.IO.File.WriteAllText(wsFile, workspaceFileContent);

            _console.WriteLine($"Workspace '{slug}' created at {wsDir}. Run 'reload' to load it.", category: "cli.workspace.new", code: "workspace.created", ctx: new Dictionary<string, object?> { ["slug"] = slug, ["dir"] = wsDir });
            return Result.Success;
        }
        catch (Exception ex) {
            _console.WriteError($"{ParksComputing.Api2Cli.Workspace.Constants.ErrorChar} Failed to create workspace: {ex.Message}", category: "cli.workspace.new", code: "workspace.create.failure", ex: ex);
            return Result.Error;
        }
    }
}
