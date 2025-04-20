using System.Text;
using System.Text.RegularExpressions;

using ParksComputing.XferKit.Scripting.Services;
using ParksComputing.XferKit.Workspace.Services;

namespace ParksComputing.XferKit.Scripting.Extensions;

public static class StringExtensions {
    public static string ReplaceXferKitPlaceholders(
        this string template,
        IPropertyResolver propertyResolver,
        ISettingsService settingsService,
        string? workspaceName = null,
        string? requestName = null,
        Dictionary<string, object?>? args = null
        ) 
    {
        // Regex pattern matches placeholders like "{{[env]::VariableName}}", "{{[prop]::VariableName}}", "{{[file]::fileName}}", or "{{VariableName::DefaultValue}}"
        string placeholderPattern = @"\{\{(\[env\]::|\[prop\]::|\[file\]::|\[arg\]::)?([^:{}]+(?::[^:{}]+)*)(?:::([^}]+))?\}\}";

        // Find all matches in the template
        var matches = Regex.Matches(template, placeholderPattern);

        foreach (Match match in matches) {
            string namespacePrefix = match.Groups[1].Value;
            var variable = match.Groups[2].Value;
            var defaultValue = match.Groups[3].Success ? match.Groups[3].Value : null;
            string? value = null;

            if (namespacePrefix == "[env]::" || string.IsNullOrEmpty(namespacePrefix)) {
                value = Environment.GetEnvironmentVariable(variable);
            }
            else if (namespacePrefix == "[arg]::") {
                if (args is not null && args.TryGetValue(variable, out var argValue) && argValue is not null) {
                    value = argValue.ToString();
                }
            }
            else if (namespacePrefix == "[prop]::") {
                value = propertyResolver.ResolveProperty(variable, workspaceName, requestName, defaultValue);
            }
            else if (namespacePrefix == "[file]::") {
                var originalDirectory = Directory.GetCurrentDirectory();
                var xferSettingsDirectory = settingsService.XferSettingsDirectory;
                string filePath = variable;

                try { 
                    if (!Path.IsPathRooted(filePath)) {
                        var currDir = Directory.GetCurrentDirectory;

                        if (!string.IsNullOrWhiteSpace(xferSettingsDirectory) && Directory.Exists(xferSettingsDirectory)) {
                            Directory.SetCurrentDirectory(xferSettingsDirectory);
                        }

                        filePath = Path.GetFullPath(filePath);
                    }

                    if (!File.Exists(filePath)) {
                        Console.Error.WriteLine($"{Workspace.Constants.ErrorChar} File not found: {filePath}");
                        value = defaultValue;
                    }

                    value = File.ReadAllText(filePath, Encoding.UTF8);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"{Workspace.Constants.ErrorChar} Error reading file '{filePath}': {ex.Message}");
                    value = defaultValue;
                }
                finally {
                    if (Directory.Exists(originalDirectory)) {
                        Directory.SetCurrentDirectory(originalDirectory);
                    }
                }
            }

            // Use defaultValue if value is still null
            value = value ?? defaultValue;

            // If a value (including a default value) is found, replace the placeholder in the template with the actual value
            if (value != null) {
                template = template.Replace(match.Value, value);
            }
        }

        return template;
    }
}
