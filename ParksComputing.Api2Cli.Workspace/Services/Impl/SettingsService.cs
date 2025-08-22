using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ParksComputing.Api2Cli.Workspace.Models;

namespace ParksComputing.Api2Cli.Workspace.Services.Impl;

internal class SettingsService : ISettingsService {
    public string? Api2CliSettingsDirectory { get; set; }
    public string ConfigFilePath { get; set; } = string.Empty;
    public string? StoreFilePath { get; set; }
    public string? PluginDirectory { get; set; }
    public string? EnvironmentFilePath { get; set; }

    public SettingsService(WorkspaceRuntimeOptions options) {
        var homeDirectory = GetUserHomeDirectory();
        // Default config root is ~/.a2c
        var defaultRoot = Path.Combine(homeDirectory, Constants.Api2CliDirectoryName);
        var configRoot = string.IsNullOrWhiteSpace(options.ConfigRoot) ? defaultRoot : options.ConfigRoot!;

        // Determine if the provided path is an existing directory, an existing file, or does not exist.
        if (File.Exists(configRoot) || Directory.Exists(configRoot)) {
            var attr = File.GetAttributes(configRoot);
            if (attr.HasFlag(FileAttributes.Directory)) {
                // It's a directory: OK
            }
            else {
                // It's a file: fail fast, we require a directory root
                var parent = Path.GetDirectoryName(configRoot) ?? defaultRoot;
                throw new InvalidOperationException($"--config must be a directory, but a file path was provided: '{configRoot}'. Use '--config '" + parent + "' instead.");
            }
        }
        else {
            // Path does not exist: create the directory root
            Directory.CreateDirectory(configRoot);
        }

        var configFilePath = Path.Combine(configRoot, Constants.WorkspacesFileName);
        var storeFilePath = Path.Combine(configRoot, Constants.StoreFileName);
        // Default packages dir is under the config root; override via options.PackagesDir
        var pluginDirectory = !string.IsNullOrWhiteSpace(options.PackagesDir)
            ? options.PackagesDir!
            : Path.Combine(configRoot, Constants.PackageDirName);
        var environmentFilePath = Path.Combine(configRoot, Constants.EnvironmentFileName);

        // Validate packages directory: allow existing directories; reject files; create when missing
        if (File.Exists(pluginDirectory) || Directory.Exists(pluginDirectory)) {
            var pAttr = File.GetAttributes(pluginDirectory);
            if (!pAttr.HasFlag(FileAttributes.Directory)) {
                throw new InvalidOperationException($"--packages must be a directory, but a file path was provided: '{pluginDirectory}'.");
            }
        } else {
            Directory.CreateDirectory(pluginDirectory);
        }

        Api2CliSettingsDirectory = configRoot;
        ConfigFilePath = configFilePath;
        StoreFilePath = storeFilePath;
        PluginDirectory = pluginDirectory;
        EnvironmentFilePath = environmentFilePath;
    }

    private static string GetUserHomeDirectory() {
        if (OperatingSystem.IsWindows()) {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
            return Environment.GetEnvironmentVariable("HOME")
                ?? throw new InvalidOperationException("HOME environment variable is not set.");
        }
        else {
            throw new PlatformNotSupportedException("Unsupported operating system.");
        }
    }
}
