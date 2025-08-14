# test-build-local.ps1 - Test the build process locally before pushing to CI

Write-Host "🧪 Testing local build with icons..." -ForegroundColor Green

# Check if icon files exist
Write-Host "📋 Checking icon files..." -ForegroundColor Yellow

$requiredIcons = @{
    "logo/icons/favicon.ico" = "Windows icon"
    "logo/icons/favicon-96x96.png" = "Linux icon"
    "logo/icons/apple-icon.png" = "macOS icon"
}

foreach ($iconPath in $requiredIcons.Keys) {
    if (-not (Test-Path $iconPath)) {
        Write-Host "❌ $($requiredIcons[$iconPath]) not found: $iconPath" -ForegroundColor Red
        exit 1
    }
}

Write-Host "✅ All icon files found" -ForegroundColor Green

# Clean previous test build
if (Test-Path "test-publish") {
    Remove-Item -Recurse -Force "test-publish"
}
New-Item -ItemType Directory -Path "test-publish" -Force | Out-Null

# Determine platform and icon
$runtime = "win-x64"
$iconFile = "favicon.ico"
$iconName = "a2c.ico"
$executable = "a2c.exe"

Write-Host "Platform: $runtime" -ForegroundColor Cyan
Write-Host "Icon file: $iconFile" -ForegroundColor Cyan

# Build the application
Write-Host "🔨 Testing build for Windows..." -ForegroundColor Cyan
dotnet publish a2c/a2c.csproj `
    --configuration Release `
    --runtime $runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o test-publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}

# Copy icon
Copy-Item "logo/icons/$iconFile" "test-publish/$iconName"

# Verify build
Write-Host "📊 Build verification:" -ForegroundColor Yellow
Get-ChildItem "test-publish" | Format-Table Name, Length, LastWriteTime

# Test if executable runs
Write-Host "🎯 Testing executable..." -ForegroundColor Cyan
try {
    $version = & "test-publish\$executable" --version 2>&1
    Write-Host "Version output: $version" -ForegroundColor Green
} catch {
    Write-Host "Note: Version command may not be implemented yet or returned non-zero exit code" -ForegroundColor Yellow
}

# Test help command
try {
    Write-Host "Testing help command..." -ForegroundColor Cyan
    $help = & "test-publish\$executable" --help 2>&1 | Select-Object -First 3
    Write-Host "Help output (first 3 lines):" -ForegroundColor Green
    $help | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
} catch {
    Write-Host "Note: Help command failed" -ForegroundColor Yellow
}

Write-Host "✅ Local build test completed successfully!" -ForegroundColor Green
Write-Host "📁 Test build available in: test-publish/" -ForegroundColor Yellow
Write-Host "🚀 Ready to push to CI/CD pipeline" -ForegroundColor Green

# Show file sizes
Write-Host "`n📊 File sizes:" -ForegroundColor Yellow
Get-ChildItem "test-publish" | Select-Object Name, @{Name="Size (MB)";Expression={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
