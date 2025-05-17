using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cliffer;

using ParksComputing.XferKit.Cli.Services.Impl;
using ParksComputing.XferKit.Scripting.Services;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Cli.Commands;

[Command("prop", "List properties in the base configuration and workspaces.")]
[Argument(typeof(string[]), "property", "One or more property names to retrieve", Cliffer.ArgumentArity.ZeroOrMore)]
internal class PropertyCommand(IWorkspaceService workspaceService, IPropertyResolver propertyResolver)
{
    public int Execute(
        [ArgumentParam("property")]string[] propertyNames
        ) 
    {
        if (propertyNames.Any()) {
            var propValue = propertyResolver.ResolveProperty(propertyNames[0]);
            Console.WriteLine($"/{propertyNames[0]}: {propValue}");
            return Result.Success;
        }

        // var workspace = workspaceService.ActiveWorkspace;
        var showAll = propertyNames.Length == 0;

        // Loop through BaseConfig properties and output them
        foreach (var prop in workspaceService.BaseConfig.Properties) {
            if (showAll || propertyNames.Contains(prop.Key, StringComparer.OrdinalIgnoreCase)) {
                Console.WriteLine($"/{prop.Key}: {prop.Value}");
            }
        }
        // Loop through workspace properties and output them
        foreach (var workspace in workspaceService.BaseConfig.Workspaces.Values) {
            foreach (var prop in workspace.Properties) {
                if (showAll || propertyNames.Contains(prop.Key, StringComparer.OrdinalIgnoreCase)) {
                    Console.WriteLine($"/{workspace.Name}/{prop.Key}: {prop.Value}");
                }
            }

            // Loop through requests and output their properties
            foreach (var request in workspace.Requests.Values) {
                if (request.Properties is not null) {
                    foreach (var prop in request.Properties) {
                        if (showAll || propertyNames.Contains(prop.Key, StringComparer.OrdinalIgnoreCase)) {
                            Console.WriteLine($"/{workspace.Name}/{request.Name}/{prop.Key}: {prop.Value}");
                        }
                    }
                }
            }
        }

        return Result.Success;
    }
}
