# XferKit GitHub Actions

This repository includes three GitHub Actions workflows for automated building and releasing:

## 🔄 Continuous Integration (CI) - `ci.yml`

**Trigger**: Automatically runs on every push or pull request to `main` branch

**What it does**:
- Builds XferKit on Windows, Linux, and macOS
- Runs tests (if any exist)
- Creates build artifacts for each platform
- Stores artifacts for 30 days

**Platforms**:
- Ubuntu (Linux)
- Windows
- macOS

## 🚀 Release Creation - `release.yml`

**Trigger**: Manual workflow dispatch (go to Actions tab → "Create Release" → "Run workflow")

**Required Input**:
- `version`: Release version number (e.g., "1.0.0")
- `prerelease`: Whether to mark as pre-release (optional)

**What it does**:
- Builds self-contained executables for all platforms
- Creates compressed archives (`.zip` for Windows, `.tar.gz` for Linux/macOS)
- Creates a GitHub Release with downloadable assets
- Automatically generates release notes

**Output Files**:
- `xk-windows-x64.zip` - Windows executable
- `xk-linux-x64.tar.gz` - Linux executable
- `xk-macos-x64.tar.gz` - macOS executable

## 📦 Installer Creation - `installers.yml`

**Trigger**: Manual workflow dispatch (go to Actions tab → "Create Installers" → "Run workflow")

**Required Input**:
- `version`: Release version number (e.g., "1.0.0")

**What it does**:
- Creates platform-specific installer packages
- Windows: MSI installer (requires WiX)
- Linux: DEB package (for Debian/Ubuntu)
- macOS: PKG installer

**Output Files**:
- `xk-windows-x64.msi` - Windows MSI installer
- `xk-linux-x64.deb` - Linux Debian package
- `xk-macos-x64.pkg` - macOS installer package

## 🎯 How to Use

### For CI (Automatic)
Just push code to main branch - CI will run automatically.

### For Releases (Manual)
1. Go to your repository on GitHub
2. Click "Actions" tab
3. Click "Create Release" workflow
4. Click "Run workflow" button
5. Enter version number (e.g., "1.2.3")
6. Choose if it's a pre-release
7. Click "Run workflow"

### For Installers (Manual)
1. Go to your repository on GitHub
2. Click "Actions" tab
3. Click "Create Installers" workflow
4. Click "Run workflow" button
5. Enter version number (e.g., "1.2.3")
6. Click "Run workflow"

## 📋 Requirements

The workflows use:
- .NET 8.0 SDK
- Modern GitHub Actions (v4)
- Self-contained publishing with trimming
- Cross-platform compatibility

## 🔧 Customization

You can modify the workflows by editing the files in `.github/workflows/`:
- `ci.yml` - Continuous integration
- `release.yml` - Release creation
- `installers.yml` - Installer packages

The workflows are configured for the `xk` project as the main executable, but can be adapted for other projects in the solution.
