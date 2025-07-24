# XferKit GitHub Actions

This repository includes automated GitHub Actions workflows for building, versioning, and releasing XferKit:

## 🔄 Auto-Version and Build - `auto-version.yml`

**Trigger**: Automatically runs on every push or pull request to `main` branch

**What it does**:
- Automatically increments version number based on commit count
- Updates project file with new version
- Creates version tags for main branch pushes
- Builds XferKit on Ubuntu, Windows, and macOS
- Runs tests and creates build artifacts
- **Provides guidance for manual release creation**

**Versioning Strategy**:
- Main branch: `MAJOR.MINOR.BUILD_NUMBER` (e.g., `0.3.45`)
- Pull requests: `MAJOR.MINOR.BUILD_NUMBER-prerelease` (e.g., `0.3.45-prerelease`)

## 🚀 Unified Release Creation - `release.yml`

**Trigger**: Manual workflow dispatch only (Actions tab → "Create Release with Installers" → "Run workflow")

**Manual Input Options**:
- `version`: Release version number (leave empty to use latest auto-generated version)
- `prerelease`: Whether to mark as pre-release (optional)

**What it does**:
- Builds self-contained executables for all platforms
- Creates **both** portable archives AND platform-specific installers
- Creates a single GitHub Release with all downloadable assets
- Automatically generates release notes

**Output Files**:

*Portable (No Installation Required)*:
- `xk-windows-x64.zip` - Windows executable
- `xk-linux-x64.tar.gz` - Linux executable
- `xk-macos-x64.tar.gz` - macOS executable

*Installers (Automatic PATH Setup)*:
- `xk-VERSION-windows-x64.msi` - Windows MSI installer
- `xk-VERSION-linux-x64.deb` - Debian/Ubuntu package
- `xk-VERSION-macos-x64.pkg` - macOS installer package

## 🎯 How to Use

### Standard Workflow (Recommended)
1. **Develop**: Make your changes and commit to any branch
2. **Merge**: Create a pull request and merge to `main` branch
3. **Auto-Version**: The system automatically increments the version number
4. **Manual Release**: When ready for release, manually trigger the "Create Release with Installers" workflow

### Manual Release Process
1. Go to your repository on GitHub
2. Click "Actions" tab
3. Click "Create Release with Installers" workflow
4. Click "Run workflow" button
5. Optionally enter custom version number (leave empty for auto-generated)
6. Choose if it's a pre-release
7. Click "Run workflow"

## 📋 Workflow Files

### Active Workflows:
- `auto-version.yml` - Auto-versioning and CI builds
- `release.yml` - Unified release creation with installers

### Removed/Empty Workflows:
- ~~`ci.yml`~~ - Consolidated into auto-version workflow
- ~~`installers.yml`~~ - Consolidated into release workflow

## 🔧 Technical Details

The workflows use:
- .NET 8.0 SDK
- Modern GitHub Actions (v4)
- Self-contained publishing with trimming
- Cross-platform compatibility
- WiX Toolset 4.0.4 for Windows MSI creation

You can modify the workflows by editing the files in `.github/workflows/`:
- `auto-version.yml` - Auto-versioning and CI builds
- `release.yml` - Unified release creation with installers

The workflows are configured for the `xk` project as the main executable, but can be adapted for other .NET projects.

## 🚀 Benefits of This Setup

- **Clean Separation**: Auto-versioning happens automatically, releases are intentional
- **No Duplication**: Single workflow creates both portable archives and installers
- **Cross-Platform**: Windows (MSI), Linux (DEB), macOS (PKG) installers
- **Full Control**: Manual release creation when you're ready
- **Professional**: Semantic versioning with proper release notes
- **No Permission Issues**: Workflows don't try to trigger each other
