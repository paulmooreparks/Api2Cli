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
using ParksComputing.Api2Cli.Scripting.Services;
using ParksComputing.Api2Cli.Workspace.Services;

namespace ParksComputing.Api2Cli.Cli.Commands;

[Command("prop", "List properties in the base configuration and workspaces.")]
[Argument(typeof(string), "property", "The name of a property to retrieve.", Cliffer.ArgumentArity.ZeroOrOne)]
internal class PropertyCommand(IWorkspaceService workspaceService, IPropertyResolver propertyResolver)
{
    public int Execute(
        [ArgumentParam("property")]string propertyName
        ) 
    {
        if (!string.IsNullOrEmpty(propertyName)) {
            var normalized = propertyResolver.NormalizePath(propertyName, workspaceService.CurrentWorkspaceName);
            var propValue = propertyResolver.ResolveProperty(normalized, workspaceService.CurrentWorkspaceName);
            Console.WriteLine($"{normalized} ({propValue?.GetType().Name ?? "null"}): {propValue ?? "null"}");
            return Result.Success;
        }

        // Loop through BaseConfig properties and output them
        foreach (var prop in workspaceService.BaseConfig.Properties) {
            Console.WriteLine($"/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}");
        }
        // Loop through workspace properties and output them
        foreach (var workspace in workspaceService.BaseConfig.Workspaces.Values) {
            foreach (var prop in workspace.Properties) {
                Console.WriteLine($"/{workspace.Name}/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}");
            }

            // Loop through requests and output their properties
            foreach (var request in workspace.Requests.Values) {
                if (request.Properties is not null) {
                    foreach (var prop in request.Properties) {
                        Console.WriteLine($"/{workspace.Name}/{request.Name}/{prop.Key} ({prop.Value.GetType().Name}): {prop.Value}");
                    }
                }
            }
        }

        return Result.Success;
    }
}
