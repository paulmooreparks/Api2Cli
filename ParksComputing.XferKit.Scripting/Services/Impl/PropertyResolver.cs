using System;
using System.Collections.Generic;
using System.Linq;

using ParksComputing.XferKit.Api;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Scripting.Services.Impl;

public class PropertyResolver(IWorkspaceService workspaceService) : IPropertyResolver {

    public string NormalizePath(string path, string? currentWorkspace = null, string? currentRequest = null) {
        if (string.IsNullOrWhiteSpace(path)){
            return string.Empty;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();

        if (!path.StartsWith('/')) {
            if (!string.IsNullOrEmpty(currentWorkspace)){
                stack.Push(currentWorkspace);
            }

            if (!string.IsNullOrEmpty(currentRequest)){
                stack.Push(currentRequest);
            }
        }

        foreach (var part in parts) {
            if (part == "..") {
                if (stack.Count > 0){
                    stack.Pop();
                }
            }
            else if (part != ".") {
                stack.Push(part);
            }
        }

        return "/" + string.Join('/', stack.Reverse());
    }

    public object? ResolveProperty(
        string path,
        string? currentWorkspace = null,
        string? currentRequest = null,
        object? defaultValue = null
    ) {
        if (string.IsNullOrWhiteSpace(path)){
            return defaultValue;
        }

        bool isRooted = path.StartsWith('/');
        var normalizedPath = NormalizePath(path, currentWorkspace, currentRequest);
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Rooted path: always resolve from base config
        if (isRooted) {
            switch (parts.Length) {
                case 1: {
                        var key = parts[0];
                        return workspaceService.BaseConfig.Properties.TryGetValue(key, out var val) ? val ?? defaultValue : defaultValue;
                }
                case 2: {
                        var wsName = parts[0];
                        var key = parts[1];

                        if (workspaceService.BaseConfig.Workspaces.TryGetValue(wsName, out var ws) &&
                            ws.Properties.TryGetValue(key, out var val)) {
                            return val ?? defaultValue;
                        }
                        
                        break;
                    }
                case 3: {
                        var wsName = parts[0];
                        var reqName = parts[1];
                        var key = parts[2];
                        
                        if (workspaceService.BaseConfig.Workspaces.TryGetValue(wsName, out var ws) &&
                            ws.Requests.TryGetValue(reqName, out var req) &&
                            req.Properties.TryGetValue(key, out var val)) {
                            return val ?? defaultValue;
                        }
                        
                        break;
                    }
            }
            return defaultValue;
        }

        // Relative path: resolve from current context outward
        switch (parts.Length) {
            case 1: {
                    var key = parts[0];

                    if (!string.IsNullOrEmpty(currentRequest) &&
                        !string.IsNullOrEmpty(currentWorkspace) &&
                        workspaceService.BaseConfig.Workspaces.TryGetValue(currentWorkspace, out var reqWs) &&
                        reqWs.Requests.TryGetValue(currentRequest, out var reqObj) &&
                        reqObj.Properties.TryGetValue(key, out var val1)
                        ) {
                        return val1 ?? defaultValue;
                    }

                    if (!string.IsNullOrEmpty(currentWorkspace) &&
                        workspaceService.BaseConfig.Workspaces.TryGetValue(currentWorkspace, out var wsObj) &&
                        wsObj.Properties.TryGetValue(key, out var val2)
                        ) {
                        return val2 ?? defaultValue;
                    }

                    if (workspaceService.BaseConfig.Properties.TryGetValue(key, out var val3)) {
                        return val3 ?? defaultValue;
                    }
                    break;
                }

            case 2: {
                    var first = parts[0];
                    var second = parts[1];

                    // In a workspace context, path could refer to a request/property
                    if (!string.IsNullOrEmpty(currentWorkspace) &&
                        workspaceService.BaseConfig.Workspaces.TryGetValue(currentWorkspace, out var wsObj) &&
                        wsObj.Requests.TryGetValue(first, out var req) &&
                        req.Properties.TryGetValue(second, out var val)
                        ) {
                        return val ?? defaultValue;
                    }

                    // Otherwise, treat first as a workspace and second as a property
                    if (workspaceService.BaseConfig.Workspaces.TryGetValue(first, out var ws) &&
                        ws.Properties.TryGetValue(second, out var val2)
                        ) {
                        return val2 ?? defaultValue;
                    }
                    break;
                }

            case 3: {
                    var wsName = parts[0];
                    var reqName = parts[1];
                    var key = parts[2];

                    if (workspaceService.BaseConfig.Workspaces.TryGetValue(wsName, out var ws) &&
                        ws.Requests.TryGetValue(reqName, out var req) &&
                        req.Properties.TryGetValue(key, out var val)
                        ) {
                        return val ?? defaultValue;
                    }
                    break;
                }
        }

        return defaultValue;
    }
}
