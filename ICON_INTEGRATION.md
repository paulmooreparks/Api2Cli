# 🎨 Icon Integration Guide for Api2Cli

This document describes how icons are integrated into Api2Cli across all platforms and build processes.

## 📁 Icon Files Used

The following icons from `logo/icons/` are used for different platforms:

| Platform | Icon File | Size | Usage |
|----------|-----------|------|-------|
| **Windows** | `favicon.ico` | 1,150 bytes | Application icon, taskbar, file explorer |
| **Linux** | `favicon-96x96.png` | 6,996 bytes | Desktop entries, file managers |
| **macOS** | `apple-icon.png` | 15,312 bytes | Dock, Finder, app bundles |

## 🔧 Integration Points

### 1. **Assembly-Level Integration** (`a2c.csproj`)

```xml
<!-- Windows application icon -->
<ApplicationIcon>..\logo\icons\favicon.ico</ApplicationIcon>

<!-- Assembly metadata -->
<AssemblyTitle>Api2Cli - API Management CLI Tool</AssemblyTitle>
<AssemblyDescription>A powerful command-line interface for HTTP API management, testing, and automation</AssemblyDescription>
<AssemblyCompany>Parks Computing</AssemblyCompany>
<AssemblyProduct>Api2Cli</AssemblyProduct>
<AssemblyCopyright>Copyright © Parks Computing 2025</AssemblyCopyright>

<!-- Embedded resources for runtime access -->
<EmbeddedResource Include="..\logo\icons\favicon.ico">
  <LogicalName>app.ico</LogicalName>
</EmbeddedResource>
<EmbeddedResource Include="..\logo\icons\apple-icon.png">
  <LogicalName>app.png</LogicalName>
</EmbeddedResource>
<EmbeddedResource Include="..\logo\icons\favicon-96x96.png">
  <LogicalName>app-96.png</LogicalName>
</EmbeddedResource>
```

### 2. **CI/CD Pipeline Integration** (`.github/workflows/auto-build.yml`)

#### Platform-Specific Build Matrix
```yaml
strategy:
  matrix:
    include:
      - os: windows-latest
        runtime: win-x64
        executable: a2c.exe
        icon_file: favicon.ico
        icon_copy_name: a2c.ico
      - os: ubuntu-latest
        runtime: linux-x64
        executable: a2c
        icon_file: favicon-96x96.png
        icon_copy_name: a2c.png
      - os: macos-latest
        runtime: osx-x64
        executable: a2c
        icon_file: apple-icon.png
        icon_copy_name: a2c.png
```

#### Icon Verification Step
```yaml
- name: Verify icon files exist
  shell: bash
  run: |
    if [ -f "logo/icons/${{ matrix.icon_file }}" ]; then
      echo "✅ Icon file found: logo/icons/${{ matrix.icon_file }}"
    else
      echo "❌ Icon file not found: logo/icons/${{ matrix.icon_file }}"
      exit 1
    fi
```

#### Icon Copy Step
```yaml
- name: Copy platform-specific icon
  shell: bash
  run: |
    cp "logo/icons/${{ matrix.icon_file }}" "publish/${{ matrix.icon_copy_name }}"
```

### 3. **Installer Integration**

#### Windows (Inno Setup)
```inno
[Setup]
SetupIconFile=publish\a2c.ico

[Icons]
Name: "{group}\Api2Cli"; Filename: "{app}\a2c.exe"; IconFilename: "{app}\a2c.ico"
Name: "{commondesktop}\Api2Cli"; Filename: "{app}\a2c.exe"; IconFilename: "{app}\a2c.ico"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
```

#### Linux (.deb)
```bash
# Copy icon to system location
cp publish/a2c.png debian/usr/share/pixmaps/

# Create desktop entry
cat > debian/usr/share/applications/api2cli.desktop << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Api2Cli
Comment=API Management CLI Tool
Exec=a2c
Icon=a2c
Terminal=true
Categories=Development;Network;
EOF
```

#### macOS (.pkg)
```bash
# Copy icon to package
cp publish/a2c.png macos-pkg/usr/local/share/pixmaps/
```

### 4. **Runtime Icon Access** (`a2c/Services/IconService.cs`)

```csharp
public static class IconService
{
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
        // ... implementation
    }
}
```

## 🏗️ Build Process

### Local Development
```bash
# PowerShell (Windows)
.\test-build-local.ps1

# Bash (Linux/macOS)
./test-build-local.sh
```

### CI/CD Pipeline
1. **Icon Verification**: Checks all required icon files exist
2. **Platform Build**: Builds executable with embedded icons
3. **Icon Copy**: Copies platform-specific icon next to executable
4. **Installer Creation**: Integrates icons into platform-specific installers
5. **Artifact Upload**: Includes both executables and icons in releases

## 📦 Distribution

### Installers
- **Windows**: `.exe` installer with icon in Start Menu and optional Desktop shortcut
- **Linux**: `.deb` package with icon in `/usr/share/pixmaps/` and desktop entry
- **macOS**: `.pkg` installer with icon in system location

### Standalone Builds
- Executable + icon file bundled together
- Icon available for external use (shortcuts, etc.)

## 🔍 Verification

### Build Verification
```bash
# Check if icon files are included
ls -la publish/a2c.*

# Verify executable works
./publish/a2c --help
```

### Runtime Verification
```csharp
// Access icon programmatically
var iconBytes = IconService.GetApplicationIcon();
var iconPath = IconService.GetIconPath();
```

## 🚀 Release Assets

Each release includes:
1. **Platform-specific installers** with full icon integration
2. **Standalone executables** with accompanying icon files
3. **Icon files** for manual integration or custom installers

## 📝 Notes

- Windows icon (`.ico`) contains multiple resolutions for different contexts
- Linux/macOS use PNG format for better quality and smaller size
- Icons are embedded as resources for runtime access
- External icon files provided for system integration
- All icons maintain consistent branding across platforms

## 🔧 Troubleshooting

### Common Issues
1. **Icon not found**: Verify file paths in build matrix
2. **Installer without icon**: Check installer script icon configuration
3. **Runtime icon access fails**: Verify embedded resource names
4. **CI/CD icon copy fails**: Check file permissions and paths

### Debugging
```bash
# Check embedded resources
dotnet reflect a2c.dll --resources

# Verify icon files in build
find publish -name "*.ico" -o -name "*.png"
```
