# build.ps1 — Build script for WorkspaceLauncher
# Usage: .\build.ps1 [-Release] [-Publish]
param(
    [switch]$Release,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot

Write-Host "=== Workspace Launcher Build ===" -ForegroundColor Cyan

# 1. Build frontend
Write-Host "`n[1/3] Building React frontend..." -ForegroundColor Yellow
$frontendDir = Join-Path $ProjectDir "frontend"

if (-not (Test-Path (Join-Path $frontendDir "node_modules"))) {
    Write-Host "  Installing npm dependencies..."
    Push-Location $frontendDir
    npm install
    Pop-Location
}

Push-Location $frontendDir
npm run build
Pop-Location

Write-Host "  Frontend build complete: frontend/dist/" -ForegroundColor Green

# 2. Build or Publish C# project
if ($Publish) {
    Write-Host "`n[2/3] Publishing single-file exe (Release, win-x64)..." -ForegroundColor Yellow
    dotnet publish $ProjectDir `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true `
        -o (Join-Path $ProjectDir "publish")

    Write-Host "  Published to: publish/WorkspaceLauncher.exe" -ForegroundColor Green
} else {
    $config = if ($Release) { "Release" } else { "Debug" }
    Write-Host "`n[2/3] Building C# project ($config)..." -ForegroundColor Yellow
    dotnet build $ProjectDir -c $config
    Write-Host "  Build complete." -ForegroundColor Green
}

Write-Host "`n[3/3] Done!" -ForegroundColor Green
Write-Host ""
Write-Host "To run in dev mode:  dotnet run" -ForegroundColor Gray
Write-Host "To publish:          .\build.ps1 -Publish" -ForegroundColor Gray
