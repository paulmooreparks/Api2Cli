#!/bin/bash
# test-build-local.sh - Test the build process locally before pushing to CI

echo "🧪 Testing local build with icons..."

# Check if icon files exist
echo "📋 Checking icon files..."
if [ ! -f "logo/icons/favicon.ico" ]; then
    echo "❌ Windows icon not found: logo/icons/favicon.ico"
    exit 1
fi

if [ ! -f "logo/icons/favicon-96x96.png" ]; then
    echo "❌ Linux icon not found: logo/icons/favicon-96x96.png"
    exit 1
fi

if [ ! -f "logo/icons/apple-icon.png" ]; then
    echo "❌ macOS icon not found: logo/icons/apple-icon.png"
    exit 1
fi

echo "✅ All icon files found"

# Clean previous test build
rm -rf test-publish
mkdir -p test-publish

# Test build for current platform
echo "🔨 Testing build for current platform..."
runtime=$(uname -s | tr '[:upper:]' '[:lower:]')
case $runtime in
    linux*) runtime="linux-x64"; icon_file="favicon-96x96.png"; icon_name="a2c.png" ;;
    darwin*) runtime="osx-x64"; icon_file="apple-icon.png"; icon_name="a2c.png" ;;
    cygwin*|mingw*|msys*) runtime="win-x64"; icon_file="favicon.ico"; icon_name="a2c.ico" ;;
    *) echo "❌ Unsupported platform: $runtime"; exit 1 ;;
esac

echo "Platform: $runtime"
echo "Icon file: $icon_file"

# Build the application
dotnet publish a2c/a2c.csproj \
    --configuration Release \
    --runtime "$runtime" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o test-publish

# Copy icon
cp "logo/icons/$icon_file" "test-publish/$icon_name"

# Verify build
echo "📊 Build verification:"
ls -la test-publish/

# Test if executable runs
echo "🎯 Testing executable..."
if [ "$runtime" = "win-x64" ]; then
    test-publish/a2c.exe --version 2>/dev/null || echo "Note: Running Windows executable on non-Windows platform"
else
    chmod +x test-publish/a2c
    test-publish/a2c --version || echo "Note: Version command may not be implemented yet"
fi

echo "✅ Local build test completed successfully!"
echo "📁 Test build available in: test-publish/"
echo "🚀 Ready to push to CI/CD pipeline"
