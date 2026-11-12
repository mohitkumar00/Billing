<#
.SYNOPSIS
    Bundles BillMate Desktop App, Backend API, and Database scripts into a standalone distribution folder.
.DESCRIPTION
    Compiles self-contained Windows x64 binaries for both the WinUI 3 desktop client and ASP.NET Core API.
    Places the API inside dist/api/ so BillMate.App.exe automatically supervises and runs it.
#>

param(
    [string]$Configuration = "Release",
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $RootDir "dist"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BillMate Standalone Desktop Bundle Builder" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Clean previous dist
if (Test-Path $DistDir) {
    Write-Host "[1/5] Cleaning existing dist directory..." -ForegroundColor Yellow
    Get-Process -Name "BillMate.App", "BillMate.Api" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Path "$DistDir\api" -Force | Out-Null
New-Item -ItemType Directory -Path "$DistDir\db" -Force | Out-Null

# 2. Publish Backend API
Write-Host "[2/5] Publishing BillMate.Api (self-contained win-x64)..." -ForegroundColor Yellow
dotnet publish "$RootDir\src\BillMate.Api\BillMate.Api.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$DistDir\api"

if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish BillMate.Api"
}

# 3. Publish Desktop App (WinUI 3 / MAUI)
Write-Host "[3/5] Publishing BillMate.App (Windows Desktop)..." -ForegroundColor Yellow
dotnet publish "$RootDir\src\BillMate.App\BillMate.App.csproj" `
    -c $Configuration `
    -f net10.0-windows10.0.19041.0 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -o $DistDir

if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish BillMate.App"
}

# 4. Copy Database & Config assets
Write-Host "[4/5] Packaging database scripts and client configurations..." -ForegroundColor Yellow
Copy-Item "$RootDir\scripts\seed-demo-data.sql" -Destination "$DistDir\db\" -Force
Copy-Item "$RootDir\scripts\init-prod-db.ps1" -Destination "$DistDir\db\" -Force

# 5. Output summary
Write-Host "[5/5] Bundle completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Distribution Location: $DistDir" -ForegroundColor Cyan
Write-Host "Primary Executable:    $DistDir\BillMate.App.exe" -ForegroundColor Green
Write-Host "Backend Sidecar:       $DistDir\api\BillMate.Api.exe" -ForegroundColor DarkGray
Write-Host ""
Write-Host "You can now run BillMate.App.exe directly without manually starting the backend." -ForegroundColor Green

if ($BuildInstaller) {
    $IsccCandidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($Iscc) {
        Write-Host "Compiling Inno Setup installer..." -ForegroundColor Yellow
        & $Iscc "$RootDir\scripts\installer.iss"
        Write-Host "Setup package built in $RootDir\setup\" -ForegroundColor Green
    } else {
        Write-Host "Inno Setup (ISCC.exe) not found. To build setup exe, install Inno Setup 6 and run:" -ForegroundColor Yellow
        Write-Host "  iscc.exe scripts\installer.iss" -ForegroundColor White
    }
}
