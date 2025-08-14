# build-with-icons.ps1 - Cross-platform build script with proper icon integration

Write-Host "🚀 Building Api2Cli with icons for all platforms..." -ForegroundColor Green

# Clean previous builds
Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "./publish") { Remove-Item -Recurse -Force "./publish" }
New-Item -ItemType Directory -Path "./publish" -Force | Out-Null

# Build for Windows (x64)
Write-Host "🪟 Building for Windows x64..." -ForegroundColor Cyan
dotnet publish a2c/a2c.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -o ./publish/win-x64

# Copy Windows icon to output (for external use)
Copy-Item "logo/icons/favicon.ico" "./publish/win-x64/a2c.ico"

# Build for Linux (x64)
Write-Host "🐧 Building for Linux x64..." -ForegroundColor Cyan
dotnet publish a2c/a2c.csproj `
    -c Release `
    -r linux-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o ./publish/linux-x64

# Copy Linux icon
Copy-Item "logo/icons/favicon-96x96.png" "./publish/linux-x64/a2c.png"

# Create Linux desktop entry
@"
[Desktop Entry]
Version=1.0
Type=Application
Name=Api2Cli
Comment=API Management CLI Tool
Exec=./a2c
Icon=./a2c.png
Terminal=true
Categories=Development;Network;
"@ | Out-File -FilePath "./publish/linux-x64/a2c.desktop" -Encoding UTF8

# Build for macOS (x64)
Write-Host "🍎 Building for macOS x64..." -ForegroundColor Cyan
dotnet publish a2c/a2c.csproj `
    -c Release `
    -r osx-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o ./publish/osx-x64

# Copy macOS icon
Copy-Item "logo/icons/apple-icon.png" "./publish/osx-x64/a2c.png"

# Build for macOS (ARM64)
Write-Host "🍎 Building for macOS ARM64..." -ForegroundColor Cyan
dotnet publish a2c/a2c.csproj `
    -c Release `
    -r osx-arm64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o ./publish/osx-arm64

# Copy macOS icon
Copy-Item "logo/icons/apple-icon.png" "./publish/osx-arm64/a2c.png"

Write-Host "✅ Build completed successfully!" -ForegroundColor Green
Write-Host "📁 Outputs available in:" -ForegroundColor Yellow
Write-Host "   • Windows x64: ./publish/win-x64/" -ForegroundColor White
Write-Host "   • Linux x64:   ./publish/linux-x64/" -ForegroundColor White
Write-Host "   • macOS x64:   ./publish/osx-x64/" -ForegroundColor White
Write-Host "   • macOS ARM64: ./publish/osx-arm64/" -ForegroundColor White

# Show file sizes
Write-Host ""
Write-Host "📊 File sizes:" -ForegroundColor Yellow
Get-ChildItem -Path "./publish" -Recurse -Filter "a2c*" | 
    Select-Object Name, @{Name="Size";Expression={[math]::Round($_.Length/1MB,2)}}, Directory |
    Format-Table -AutoSize
