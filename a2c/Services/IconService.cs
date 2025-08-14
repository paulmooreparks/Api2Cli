using System.Reflection;
using System.Runtime.InteropServices;

namespace ParksComputing.Api2Cli.Cli.Services;

/// <summary>
/// Service for accessing application icons and resources
/// </summary>
public static class IconService
{
    /// <summary>
    /// Gets the application icon as a byte array for the current platform
    /// </summary>
    /// <returns>Icon bytes or null if not found</returns>
    public static byte[]? GetApplicationIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var platform = Environment.OSVersion.Platform;
        
        string resourceName = platform switch
        {
            PlatformID.Win32NT => "app.ico",
            PlatformID.Unix when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => "app.png",
            PlatformID.Unix => "app-96.png",
            _ => "app.ico"
        };
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        
        var buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        return buffer;
    }
    
    /// <summary>
    /// Gets the path to the application icon file for the current platform
    /// </summary>
    /// <returns>Icon file path or null</returns>
    public static string? GetIconPath()
    {
        var platform = Environment.OSVersion.Platform;
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        return platform switch
        {
            PlatformID.Win32NT => Path.Combine(appDirectory!, "a2c.ico"),
            PlatformID.Unix => Path.Combine(appDirectory!, "a2c.png"),
            _ => null
        };
    }
}
