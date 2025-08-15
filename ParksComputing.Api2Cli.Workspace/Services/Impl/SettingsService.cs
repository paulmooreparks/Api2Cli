using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParksComputing.Api2Cli.Workspace.Services.Impl;

internal class SettingsService : ISettingsService {
    public string? Api2CliSettingsDirectory { get; set; }
    public string ConfigFilePath { get; set; } = string.Empty;
    public string? StoreFilePath { get; set; }
    public string? PluginDirectory { get; set; }
    public string? EnvironmentFilePath { get; set; }

    public SettingsService() {
        string homeDirectory = GetUserHomeDirectory();
        string a2cDirectory = Path.Combine(homeDirectory, Constants.Api2CliDirectoryName);

        if (!Directory.Exists(a2cDirectory)) {
            Directory.CreateDirectory(a2cDirectory);
        }

        // Allow overriding the workspace config file via env var (set by --config in CLI)
        string? overrideConfig = Environment.GetEnvironmentVariable("A2C_WORKSPACE_CONFIG");
        string configFilePath = !string.IsNullOrWhiteSpace(overrideConfig)
            ? overrideConfig
            : Path.Combine(a2cDirectory, Constants.WorkspacesFileName);
        string storeFilePath = Path.Combine(a2cDirectory, Constants.StoreFileName);
        string pluginDirectory = Path.Combine(a2cDirectory, Constants.PackageDirName);
        string environmentFilePath = Path.Combine(a2cDirectory, Constants.EnvironmentFileName);

        if (!Directory.Exists(pluginDirectory)) {
            Directory.CreateDirectory(pluginDirectory);
        }

        Api2CliSettingsDirectory = a2cDirectory;
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
