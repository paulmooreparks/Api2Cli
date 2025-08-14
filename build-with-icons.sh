#!/bin/bash
# build-with-icons.sh - Cross-platform build script with proper icon integration

set -e

echo "🚀 Building Api2Cli with icons for all platforms..."

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf ./publish
mkdir -p ./publish

# Build for Windows (x64)
echo "🪟 Building for Windows x64..."
dotnet publish a2c/a2c.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:PublishTrimmed=false \
    -o ./publish/win-x64

# Copy Windows icon to output (for external use)
cp logo/icons/favicon.ico ./publish/win-x64/a2c.ico

# Build for Linux (x64)
echo "🐧 Building for Linux x64..."
dotnet publish a2c/a2c.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o ./publish/linux-x64

# Copy Linux icon
cp logo/icons/favicon-96x96.png ./publish/linux-x64/a2c.png

# Create Linux desktop entry
cat > ./publish/linux-x64/a2c.desktop << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Api2Cli
Comment=API Management CLI Tool
Exec=./a2c
Icon=./a2c.png
Terminal=true
Categories=Development;Network;
EOF

# Build for macOS (x64)
echo "🍎 Building for macOS x64..."
dotnet publish a2c/a2c.csproj \
    -c Release \
    -r osx-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o ./publish/osx-x64

# Copy macOS icon
cp logo/icons/apple-icon.png ./publish/osx-x64/a2c.png

# Build for macOS (ARM64)
echo "🍎 Building for macOS ARM64..."
dotnet publish a2c/a2c.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o ./publish/osx-arm64

# Copy macOS icon
cp logo/icons/apple-icon.png ./publish/osx-arm64/a2c.png

# Make executables executable on Unix systems
chmod +x ./publish/linux-x64/a2c
chmod +x ./publish/osx-x64/a2c
chmod +x ./publish/osx-arm64/a2c

echo "✅ Build completed successfully!"
echo "📁 Outputs available in:"
echo "   • Windows x64: ./publish/win-x64/"
echo "   • Linux x64:   ./publish/linux-x64/"
echo "   • macOS x64:   ./publish/osx-x64/"
echo "   • macOS ARM64: ./publish/osx-arm64/"

# Show file sizes
echo ""
echo "📊 File sizes:"
find ./publish -name "a2c*" -type f -exec ls -lh {} \; | awk '{print $5 "\t" $9}'
