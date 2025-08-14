using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Scripting.Services;

public interface IPropertyResolver {
    string NormalizePath(string path, string? currentWorkspace = null, string? currentRequest = null);
    object? ResolveProperty(string path, string? currentWorkspace = null, string? currentRequest = null, object? defaultValue = null);
}
