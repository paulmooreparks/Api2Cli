using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using Microsoft.ClearScript;

using ParksComputing.Api2Cli.Cli.Services.Impl;
using ParksComputing.Api2Cli.Cli.Services;
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("prop", "List properties in the base configuration and workspaces.")]
[Argument(typeof(string), "property", "The name of a property to retrieve.", Cliffer.ArgumentArity.ZeroOrOne)]
internal class PropertyCommand(IWorkspaceService workspaceService, IPropertyResolver propertyResolver, IConsoleWriter consoleWriter)
{
    public int Execute(
        [ArgumentParam("property")]string propertyName
        )
    {
        if (!string.IsNullOrEmpty(propertyName)) {
            var normalized = propertyResolver.NormalizePath(propertyName, workspaceService.CurrentWorkspaceName);
            var propValue = propertyResolver.ResolveProperty(normalized, workspaceService.CurrentWorkspaceName);
            consoleWriter.WriteLine($"{normalized} ({propValue?.GetType().Name ?? "null"}): {propValue ?? "null"}", category: "cli.property", code: "property.single");
            return Result.Success;
        }

        // Loop through BaseConfig properties and output them
        foreach (var prop in workspaceService.BaseConfig.Properties) {
            consoleWriter.WriteLine($"/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}", category: "cli.property", code: "property.base");
        }
        // Loop through workspace properties and output them
        foreach (var workspace in workspaceService.BaseConfig.Workspaces.Values) {
            foreach (var prop in workspace.Properties) {
                consoleWriter.WriteLine($"/{workspace.Name}/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}", category: "cli.property", code: "property.workspace");
            }

            // Loop through requests and output their properties
            foreach (var request in workspace.Requests.Values) {
                if (request.Properties is not null) {
                    foreach (var prop in request.Properties) {
                        consoleWriter.WriteLine($"/{workspace.Name}/{request.Name}/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}", category: "cli.property", code: "property.request");
                    }
                }
            }
        }

        return Result.Success;
    }
}
