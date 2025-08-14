namespace ParksComputing.Api2Cli.Workspace.Services;

public interface ISettingsService
{
    string? Api2CliSettingsDirectory { get; set; }
    string ConfigFilePath { get; set; }
    string? StoreFilePath { get; set; }
    string? PluginDirectory { get; set; }
    string? EnvironmentFilePath { get; set; }
}
