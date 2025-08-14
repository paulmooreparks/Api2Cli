using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Scripting.Api.Package;

public interface IPackageApi
{
    [ScriptMember("install")]
    PackageApiResult Install(string packageName);
    [ScriptMember("installAsync")]
    Task<PackageApiResult> InstallAsync(string packageName);
    [ScriptMember("uninstall")]
    PackageApiResult Uninstall(string packageName);
    [ScriptMember("uninstallAsync")]
    Task<PackageApiResult> UninstallAsync(string packageName);
    [ScriptMember("update")]
    PackageApiResult Update(string packageName);
    [ScriptMember("updateAsync")]
    Task<PackageApiResult> UpdateAsync(string packageName);
    [ScriptMember("search")]
    PackageApiResult Search(string search);
    [ScriptMember("searchAsync")]
    Task<PackageApiResult> SearchAsync(string search);
    [ScriptMember("list")]
    string[] List { get; }
}

public class PackageApiResult
{
    [ScriptMember("success")]
    public bool Success { get; set; }
    [ScriptMember("message")]
    public string? Message { get; set; }
    [ScriptMember("packageName")]
    public string? PackageName { get; set; }
    [ScriptMember("version")]
    public string? Version { get; set; }
    [ScriptMember("path")]
    public string? Path { get; set; }
    [ScriptMember("list")]
    public string[]? List { get; set; }
}
