# XferKit GitHub Actions

This repository includes automated GitHub Actions workflows for building, versioning, and releasing XferKit:

## 🔄 Auto-Version and Build - `auto-version.yml`

**Trigger**: Automatically runs on every push or pull request to `main` branch

**What it does**:
- Automatically increments version number based on commit count
- Updates project file with new version
- Creates version tags for main branch pushes
- Builds XferKit on Ubuntu, Windows, and macOS
- Runs tests (if any exist)
- Creates build artifacts for each platform
- **Automatically triggers release creation for main branch pushes**

**Versioning Strategy**:
- Main branch: `MAJOR.MINOR.BUILD_NUMBER` (e.g., `0.3.45`)
- Pull requests: `MAJOR.MINOR.BUILD_NUMBER-prerelease` (e.g., `0.3.45-prerelease`)

## 🚀 Unified Release Creation - `release.yml`

**Trigger**:
- Automatically triggered after successful auto-version on main branch
- Manual workflow dispatch (Actions tab → "Create Release with Installers" → "Run workflow")

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

**Output Files**:
- `xk-windows-x64.msi` - Windows MSI installer
- `xk-linux-x64.deb` - Linux Debian package
- `xk-macos-x64.pkg` - macOS installer package

## 🎯 How to Use

### Automatic Process (Recommended)
1. **Develop**: Make your changes and commit to any branch
2. **Merge**: Create a pull request and merge to `main` branch
3. **Auto-magic**: The system automatically:
   - Increments the version number
   - Builds cross-platform
   - Creates and publishes a complete release with both portable archives and installers

### Manual Release (Optional)
If you need to create a release manually or with a specific version:

1. Go to your repository on GitHub
2. Click "Actions" tab
3. Click "Create Release with Installers" workflow
4. Click "Run workflow" button
5. Optionally enter custom version number (leave empty for auto-generated)
6. Choose if it's a pre-release
7. Click "Run workflow"

## 📋 Requirements

The workflows use:
- .NET 8.0 SDK
- Modern GitHub Actions (v4)
- Self-contained publishing with trimming
- Cross-platform compatibility
- WiX Toolset 4.0.4 for Windows MSI creation

## 🔧 Customization

You can modify the workflows by editing the files in `.github/workflows/`:
- `auto-version.yml` - Auto-versioning and CI builds
- `release.yml` - Unified release creation with installers

The workflows are configured for the `xk` project as the main executable, but can be adapted for other .NET projects.

## 🚀 Benefits of This Setup

- **Fully Automated**: Push to main → automatic version increment → automatic release
- **No Duplication**: Single workflow creates both portable archives and installers
- **Cross-Platform**: Windows (MSI), Linux (DEB), macOS (PKG) installers
- **Flexible**: Manual override available when needed
- **Professional**: Semantic versioning with proper release notes
