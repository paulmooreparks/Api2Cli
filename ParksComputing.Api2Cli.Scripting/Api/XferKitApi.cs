using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;

using Microsoft.ClearScript;

using ParksComputing.Api2Cli.Api.Http;
using ParksComputing.Api2Cli.Api.Store;
using ParksComputing.Api2Cli.Scripting.Api.FileSystem;
using ParksComputing.Api2Cli.Scripting.Api.Package;
using ParksComputing.Api2Cli.Scripting.Api.Process;
using ParksComputing.Api2Cli.Workspace.Models;
using ParksComputing.Api2Cli.Workspace.Services;

// using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Api;

public class A2CApi : DynamicObject {
    private readonly Dictionary<string, object?> _properties = new();
    private readonly IWorkspaceService _workspaceService;

    [ScriptMember("workspaceList")]
    public IEnumerable<string> WorkspaceList => _workspaceService.WorkspaceList;

    [ScriptMember("currentWorkspaceName")]
    public string CurrentWorkspaceName => _workspaceService.CurrentWorkspaceName;

    [ScriptMember("http")]
    public IHttpApi Http { get; }

    [ScriptMember("store")]
    public IStoreApi Store { get; }

    [ScriptMember("package")]
    public IPackageApi Package { get; }

    [ScriptMember("process")]
    public IProcessApi Process { get; }

    [ScriptMember("fileSystem")]
    public IFileSystemApi FileSystem { get; }

    [ScriptMember("workspaces")]
    public dynamic Workspaces { get; } = new ExpandoObject() as dynamic;

    public A2CApi(
        IWorkspaceService workspaceService,
        IHttpApi httpApi,
        IStoreApi storeApi,
        IPackageApi packageApi,
        IProcessApi processApi,
        IFileSystemApi fileSystemApi
        )
    {
        _workspaceService = workspaceService;
        var workspacesDict = new ExpandoObject() as IDictionary<string, object>;

#if false
        foreach (var workspaceKvp in _workspaceService.BaseConfig?.Workspaces ?? []) {
            workspacesDict[workspaceKvp.Key] = workspaceKvp.Value;
        }

        workspaces = workspacesDict;
#endif

        Http = httpApi;
        Store = storeApi;
        Package = packageApi;
        Process = processApi;
        FileSystem = fileSystemApi;
    }


    public object? this[string key] {
        get {
            if (string.IsNullOrEmpty(key)) {
                throw new ArgumentNullException(nameof(key));
            }

            if (_properties.TryGetValue(key, out object? value)) {
                return value;
            }

            return default;
        }
        set {
            if (value is null) {
                _properties.Remove(key);
            }
            else {
                _properties[key] = value;
            }
        }
    }

    [ScriptMember("setActiveWorkspace")]
    public void SetActiveWorkspace(string workspaceName) {
        _workspaceService.SetActiveWorkspace(workspaceName);
    }

    [ScriptMember("activeWorkspace")]
    public WorkspaceDefinition ActiveWorkspace => _workspaceService.ActiveWorkspace;

    [ScriptMember("trySetProperty")]
    public bool TrySetProperty(string name, object? value) {
        return _properties.TryAdd(name, value);
    }

    [ScriptMember("tryGetProperty")]
    public bool TryGetProperty(string name, out object? value) {
        return _properties.TryGetValue(name, out value);
    }

    [ScriptMember("tryGetMember")]
    public override bool TryGetMember(GetMemberBinder binder, out object? result) {
        return _properties.TryGetValue(binder.Name, out result);
    }

    [ScriptMember("trySetMember")]
    public override bool TrySetMember(SetMemberBinder binder, object? value) {
        _properties[binder.Name] = value;
        return true;
    }

    [ScriptMember("getDynamicMemberNames")]
    public override IEnumerable<string> GetDynamicMemberNames() => _properties.Keys;
}
