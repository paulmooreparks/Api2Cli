Param(
    [switch]$AggressiveTypeRenames,
    [switch]$NoCommit,
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step($msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Update-InFiles {
    Param(
        [Parameter(Mandatory=$true)][string[]]$Paths,
        [Parameter(Mandatory=$true)][string[]]$Include,
        [Parameter(Mandatory=$true)][hashtable]$Map,
        [switch]$UseRegex
    )
    foreach ($p in $Paths) {
        if (-not (Test-Path $p)) { continue }
        Get-ChildItem -Path $p -Recurse -File -Include $Include | ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop
            $orig = $content
            foreach ($k in $Map.Keys) {
                if ($UseRegex) {
                    $content = $content -replace $k, $Map[$k]
                } else {
                    $content = $content -replace [regex]::Escape($k), [System.Text.RegularExpressions.Regex]::Escape($Map[$k]) -replace '\\([.$^{\[(|)*+?\\])', '$1'
                }
            }
            if ($content -ne $orig) {
                if ($WhatIf) {
                    Write-Host "Would update: $($_.FullName)" -ForegroundColor Yellow
                } else {
                    [System.IO.File]::WriteAllText($_.FullName, $content, [System.Text.UTF8Encoding]::new($false))
                    Write-Host "Updated: $($_.FullName)" -ForegroundColor DarkGreen
                }
            }
        }
    }
}

function Test-RepoRoot {
    if (-not (Test-Path -LiteralPath './XferKit.sln' -PathType Leaf) -and -not (Test-Path -LiteralPath './Api2Cli.sln' -PathType Leaf)) {
        throw "Run this script from the repository root (XferKit.sln or Api2Cli.sln not found)."
    }
}

function Test-GitClean {
    $status = git status --porcelain
    if ($status) {
        Write-Warning "Git working tree not clean. Commit or stash before running."
    }
}

Test-RepoRoot
Test-GitClean

# Step 0: Close VS Code before running to avoid Windows locks on ./xk

# Step 1: Remove old project from solution (before moving files)
if (Test-Path ./XferKit.sln) {
    Write-Step "Removing xk/xk.csproj from XferKit.sln"
    if (-not $WhatIf) { dotnet sln XferKit.sln remove .\xk\xk.csproj | Out-Host }
}

# Step 2: Rename solution file
if (Test-Path ./XferKit.sln) {
    Write-Step "Renaming XferKit.sln -> Api2Cli.sln"
    if ($WhatIf) {
        Write-Host "Would: git mv XferKit.sln Api2Cli.sln" -ForegroundColor Yellow
    } else {
        git mv XferKit.sln Api2Cli.sln | Out-Host
    }
}

# Step 3: Rename CLI folder and project
if (Test-Path ./xk) {
    Write-Step "Renaming folder xk -> a2c"
    if ($WhatIf) { Write-Host "Would: git mv xk a2c" -ForegroundColor Yellow } else { git mv xk a2c | Out-Host }
}
if (Test-Path ./a2c/xk.csproj) {
    Write-Step "Renaming a2c/xk.csproj -> a2c/a2c.csproj"
    if ($WhatIf) { Write-Host "Would: git mv a2c/xk.csproj a2c/a2c.csproj" -ForegroundColor Yellow } else { git mv a2c/xk.csproj a2c/a2c.csproj | Out-Host }
}

# Step 4: Add new project to solution
if (Test-Path ./Api2Cli.sln) {
    Write-Step "Adding a2c/a2c.csproj to Api2Cli.sln"
    if (-not $WhatIf) { dotnet sln Api2Cli.sln add .\a2c\a2c.csproj | Out-Host }
}

# Step 5: Update VS Code configs
Write-Step "Updating VS Code settings/tasks/launch"
Update-InFiles -Paths @('.vscode') -Include @('settings.json') -Map @{
    'XferKit.sln' = 'Api2Cli.sln'
}
Update-InFiles -Paths @('.vscode') -Include @('tasks.json') -Map @{
    'run xk' = 'run a2c';
    '"--project",\s*"xk"' = '"--project","a2c"';
    'XferKit.sln' = 'Api2Cli.sln'
} -UseRegex
Update-InFiles -Paths @('.vscode') -Include @('launch.json') -Map @{
    '/xk/bin/Debug/net8.0/xk.dll' = '/a2c/bin/Debug/net8.0/a2c.dll';
    '"cwd":\s*"\$\{workspaceFolder}/xk"' = '"cwd": "${workspaceFolder}/a2c"';
    '.NET Run xk' = '.NET Run a2c'
} -UseRegex

# Step 6: Update GitHub workflows and docs
Write-Step "Updating GitHub workflow names and binary/project paths"
Update-InFiles -Paths @('.github') -Include @('*.yml','*.yaml','*.md') -Map @{
    'xk/xk.csproj' = 'a2c/a2c.csproj';
    ' XferKit ' = ' Api2Cli ';
    'XferKit-' = 'Api2Cli-';
    'xk.exe' = 'a2c.exe';
    ' usr/local/bin/xk' = ' usr/local/bin/a2c';
    'Package: xferkit' = 'Package: api2cli';
    'identifier com.yourorg.xferkit' = 'identifier com.yourorg.api2cli';
    'release_name: XferKit' = 'release_name: Api2Cli';
    'asset_name: XferKit' = 'asset_name: Api2Cli';
    'asset_path: release-assets/XferKit' = 'asset_path: release-assets/Api2Cli';
}

# Step 7: Rename solution folder entries
if (Test-Path ./Api2Cli.sln) {
    Write-Step "Patching solution content (.xk -> .a2c; XferKit -> Api2Cli)"
    Update-InFiles -Paths @('.') -Include @('Api2Cli.sln') -Map @{
        '.xk' = '.a2c';
        'XferKit' = 'Api2Cli'
    }
}

# Step 8: Namespace and product renames in source
Write-Step "Renaming namespaces ParksComputing.XferKit.* -> ParksComputing.Api2Cli.*"
Update-InFiles -Paths @('.') -Include @('*.cs','*.csproj','*.props','*.targets') -Map @{
    'ParksComputing.XferKit.' = 'ParksComputing.Api2Cli.';
    'ParksComputing.XferKit' = 'ParksComputing.Api2Cli'
}

Write-Step "Updating product strings: XferKit -> Api2Cli"
Update-InFiles -Paths @('.') -Include @('*.cs','*.csproj','*.md','*.yml','*.yaml','*.json') -Map @{
    'XferKit' = 'Api2Cli'
}

Write-Step "Updating CLI short name: xk -> a2c in code and scripts"
Update-InFiles -Paths @('.') -Include @('*.cs','*.js','*.json','*.md','*.yml','*.yaml') -Map @{
    'AddHostObject("xk"' = 'AddHostObject("a2c"';
    '"xk", _xk' = '"a2c", _xk';
    ' xk.' = ' a2c.';
    '"xk"' = '"a2c"'
}

# Step 9: Config/Constants adjustments (.xk -> .a2c)
Write-Step "Updating config directories (.xk -> .a2c)"
Update-InFiles -Paths @('.') -Include @('*.cs','*.json','*.md','*.yml','*.yaml') -Map @{
    '.xk' = '.a2c'
}

if (Test-Path ./.xk) {
    Write-Step "Renaming repo folder .xk -> .a2c"
    if ($WhatIf) { Write-Host "Would: git mv .xk .a2c" -ForegroundColor Yellow } else { git mv .xk .a2c | Out-Host }
}

# Optional: Aggressive type renames (class names, fields)
if ($AggressiveTypeRenames) {
    Write-Step "Aggressive type renames (XferKitApi -> Api2CliApi, _xk -> _a2c)"
    Replace-InFiles -Paths @('.') -Include @('*.cs') -Map @{
        'class XferKitApi' = 'class Api2CliApi';
        'new XferKitApi' = 'new Api2CliApi';
        ' XferKitApi ' = ' Api2CliApi ';
        '_xk' = '_a2c'
    }
}

# Step 10: Ensure CLI AssemblyName remains a2c
Write-Step "Ensuring a2c AssemblyName in a2c/a2c.csproj"
if (Test-Path ./a2c/a2c.csproj) {
    Update-InFiles -Paths @('./a2c') -Include @('a2c.csproj') -Map @{
        '<AssemblyName>.*?</AssemblyName>' = '<AssemblyName>a2c</AssemblyName>'
    } -UseRegex
}

# Step 11: Restore and build
Write-Step "dotnet restore"
if (-not $WhatIf) { dotnet restore Api2Cli.sln | Out-Host }
Write-Step "dotnet build"
if (-not $WhatIf) { dotnet build Api2Cli.sln | Out-Host }

# Step 12: Smoke test version
if (-not $WhatIf) {
    Write-Step "dotnet run --project a2c -- --version"
    dotnet run --project a2c -- --version | Out-Host
}

# Step 13: Commit changes
if (-not $NoCommit) {
    Write-Step "git add and commit"
    if ($WhatIf) {
        Write-Host "Would: git add -A; git commit -m 'Rename XferKit->Api2Cli; xk->a2c'" -ForegroundColor Yellow
    } else {
        git add -A | Out-Host
        git commit -m "Rename XferKit to Api2Cli; xk to a2c" | Out-Host
    }
}

Write-Host "Done. Reopen in VS Code and verify." -ForegroundColor Green
