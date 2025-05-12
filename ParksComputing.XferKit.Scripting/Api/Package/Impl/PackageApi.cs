using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ParksComputing.XferKit.Scripting.Api.Package;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Scripting.Api.Package.Impl;
internal class PackageApi : IPackageApi
{
    private readonly IPackageService _packageService;

    public PackageApi(
        IPackageService packageService
        )
    {
        _packageService = packageService;
    }

    public string[] List
    {
        get
        {
            var list = new List<string>();

            var plugins = _packageService.GetInstalledPackages();
            if (plugins is not null && plugins.Count() > 0)
            {
                foreach (var plugin in plugins)
                {
                    list.Add($"{plugin}");
                }
            }

            return [.. list];
        }
    }

    public async Task<PackageApiResult> SearchAsync(string search)
    {
        var result = new PackageApiResult();
        var list = new List<string>();
        var searchResult = await _packageService.SearchPackagesAsync(search);

        if (searchResult.Success == false)
        {
            result.Success = false;
            result.Message = $"Error searching for packages: {searchResult.ErrorMessage}";
        }
        else if (searchResult.Items is null || searchResult.Items.Count() == 0)
        {
            result.Success = false;
            result.Message = $"No results found for search term '{search}'.";
        }
        else
        {
            foreach (var package in searchResult.Items)
            {
                list.Add($"{package.Name} ({package.Version}) {package.Description}");
            }

            result.Success = true;
            result.List = list.ToArray();
        }

        return result;
    }

    public PackageApiResult Search(string search)
    {
        return SearchAsync(search).GetAwaiter().GetResult();
    }

    public async Task<PackageApiResult> InstallAsync(string packageName)
    {
        var result = new PackageApiResult();

        var packageInstallResult = await _packageService.InstallPackageAsync(packageName);

        if (packageInstallResult == null)
        {
            result.Success = false;
            result.Message = $"Unexpected error installing package '{packageName}'.";
        }
        else if (packageInstallResult.Success)
        {
            result.Success = true;
            result.PackageName = packageInstallResult.ConfirmedPackageName;
            result.Version = packageInstallResult.Version;
            result.Path = packageInstallResult.Path;
            result.Message = $"Installed {packageInstallResult.ConfirmedPackageName} {packageInstallResult.Version} to {packageInstallResult.Path}";
        }
        else
        {
            result.Success = false;
            result.Message = $"Failed to install package '{packageName}': {packageInstallResult.ErrorMessage}";
        }

        return result;
    }

    public PackageApiResult Install(string packageName)
    {
        return InstallAsync(packageName).GetAwaiter().GetResult();
    }

    public async Task<PackageApiResult> UninstallAsync(string packageName)
    {
        var result = new PackageApiResult();

        var uninstallResult = await _packageService.UninstallPackageAsync(packageName);

        switch (uninstallResult)
        {
            case PackageUninstallResult.Success:
                result.Success = true;
                result.Message = $"Uninstalled {packageName}";
                break;

            case PackageUninstallResult.NotFound:
                result.Success = false;
                result.Message = $"Package {packageName} is not installed.";
                break;

            case PackageUninstallResult.Failed:
            default:
                result.Success = false;
                result.Message = $"Error uninstalling package '{packageName}'.";
                break;
        }

        return result;
    }

    public PackageApiResult Uninstall(string packageName)
    {
        return UninstallAsync(packageName).GetAwaiter().GetResult();
    }

    public async Task<PackageApiResult> UpdateAsync(string packageName)
    {
        var result = new PackageApiResult();

        var packageInstallResult = await _packageService.UpdatePackageAsync(packageName);

        if (packageInstallResult == null)
        {
            result.Success = false;
            result.Message = $"Unexpected error updating package '{Install}'.";
        }
        else if (packageInstallResult.Success)
        {
            result.Success = true;
            result.PackageName = packageInstallResult.ConfirmedPackageName;
            result.Version = packageInstallResult.Version;
            result.Path = packageInstallResult.Path;
            result.Message = $"Updated {packageInstallResult.ConfirmedPackageName} to {packageInstallResult.Version}";
        }
        else
        {
            result.Success = false;
            result.Message = $"Failed to update package '{Install}': {packageInstallResult.ErrorMessage}";
        }

        return result;
    }

    public PackageApiResult Update(string packageName)
    {
        return UpdateAsync(packageName).GetAwaiter().GetResult();
    }
}
