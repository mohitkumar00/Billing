<#
.SYNOPSIS
    Builds and packages the Android APK for BillMate POS.
.DESCRIPTION
    Checks for the Android SDK and Java JDK, configures environment paths,
    and runs dotnet publish for net10.0-android.
#>

param(
    [string]$Configuration = "Release",
    [string]$AndroidSdkPath = "$env:LOCALAPPDATA\Android\Sdk",
    [string]$OutputDir = "dist\android"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " BillMate Android APK Build Script       " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Check for Android SDK
if (-not (Test-Path $AndroidSdkPath)) {
    $fallbackPaths = @(
        "$env:LOCALAPPDATA\Android\Sdk",
        "C:\Program Files (x86)\Android\android-sdk",
        "$env:ANDROID_HOME",
        "$env:ANDROID_SDK_ROOT"
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($fallbackPaths.Count -gt 0) {
        $AndroidSdkPath = $fallbackPaths[0]
    }
}

if (-not (Test-Path $AndroidSdkPath)) {
    Write-Host ""
    Write-Host "❌ Android SDK not detected on this machine." -ForegroundColor Red
    Write-Host ""
    Write-Host "To install the Android SDK on Windows:" -ForegroundColor Yellow
    Write-Host "  1. Option A (Automated via winget):" -ForegroundColor White
    Write-Host "     winget install Google.AndroidStudio" -ForegroundColor Cyan
    Write-Host "  2. Option B (Command-line tools):" -ForegroundColor White
    Write-Host "     Install Java JDK 17+: winget install Microsoft.OpenJDK.17" -ForegroundColor Cyan
    Write-Host "     Download Android Command-Line Tools from: https://developer.android.com/studio#command-line-tools-only" -ForegroundColor Cyan
    Write-Host "     Extract to $env:LOCALAPPDATA\Android\Sdk\cmdline-tools\latest" -ForegroundColor Cyan
    Write-Host "     Run: sdkmanager 'platforms;android-34' 'build-tools;34.0.0'" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "After installing Android SDK, rerun this script:" -ForegroundColor Green
    Write-Host "  .\scripts\build-android.ps1 -AndroidSdkPath 'C:\Users\YourUser\AppData\Local\Android\Sdk'" -ForegroundColor White
    exit 1
}

Write-Host "Using Android SDK at: $AndroidSdkPath" -ForegroundColor Green
$env:ANDROID_HOME = $AndroidSdkPath
$env:ANDROID_SDK_ROOT = $AndroidSdkPath

# 2. Build and publish Android APK
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "Publishing BillMate Android APK ($Configuration)..." -ForegroundColor Yellow
dotnet publish "src\BillMate.App\BillMate.App.csproj" `
    -f net10.0-android `
    -c $Configuration `
    -p:AndroidSdkDirectory="$AndroidSdkPath" `
    -o $OutputDir

Write-Host ""
Write-Host "✅ Android APK successfully published to $OutputDir" -ForegroundColor Green
Write-Host "To install on your connected Android tablet/phone via USB:" -ForegroundColor Cyan
Write-Host "  adb install $OutputDir\com.billmate.pos-Signed.apk" -ForegroundColor White
